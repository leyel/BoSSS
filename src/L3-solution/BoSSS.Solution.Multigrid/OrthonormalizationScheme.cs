﻿/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ilPSP.LinSolvers;
using ilPSP;
using ilPSP.Utils;
using BoSSS.Platform;
using BoSSS.Platform.Utils;
using System.Diagnostics;
using MPI.Wrappers;


namespace BoSSS.Solution.Multigrid {

    /// <summary>
    /// Memory-intensive ortho-normalization scheme;should converge with similar rate as GMRES, 
    /// but is able to use multiple pre-conditioners, see <see cref="PrecondS"/>.
    /// </summary>
    public class OrthonormalizationScheme : ISolverSmootherTemplate, ISolverWithCallback {
        
        MultigridOperator m_mgop;

        public void Init(MultigridOperator op) {
            var M = op.OperatorMatrix;
            var MgMap = op.Mapping;
            this.m_mgop = op;

            if(!M.RowPartitioning.EqualsPartition(MgMap.Partitioning))
                throw new ArgumentException("Row partitioning mismatch.");
            if(!M.ColPartition.EqualsPartition(MgMap.Partitioning))
                throw new ArgumentException("Column partitioning mismatch.");

            foreach(var pc in PrecondS) {
                pc.Init(m_mgop);
            }
        }

        
        public ISolverSmootherTemplate[] PrecondS;

     

        public void Solve<U, V>(U X, V B)
            where U : IList<double>
            where V : IList<double> //
        {
            // init 
            // ====

            int L = this.m_mgop.Mapping.LocalLength;
            if(X.Count != L)
                throw new ArgumentException();
            if(B.Count != L)
                throw new ArgumentException();

            var Mtx = this.m_mgop.OperatorMatrix;

            
            // residual of initial guess
            // =========================

            // history of solutions and residuals (max vector length 'MaxKrylovDim')
            List<double[]> SolHistory = new List<double[]>();
            List<double[]> MxxHistory = new List<double[]>();

            double[] Correction = new double[L];
            double[] Mxx = new double[L];
            double[] CurrentSol = new double[L];
            double[] CurrentRes = new double[L];

            CurrentSol.SetV(X, 1.0);
            CurrentRes.SetV(B, 1.0);
            Mtx.SpMV(-1.0, CurrentSol, 1.0, CurrentRes);
            int KrylovDim = 0;

            double[] Residual0 = CurrentRes.CloneAs();
            double[] Solution0 = CurrentSol.CloneAs();

            List<double> _R = new List<double>();

            // diagnostic output
            if(this.IterationCallback != null)
                this.IterationCallback(0, CurrentSol.CloneAs(), CurrentRes.CloneAs(), this.m_mgop);

            // iterations...
            // =============
            double[] PreviousRes = new double[L];

            //MultidimensionalArray raw_Mxx = MultidimensionalArray.Create(L, MaxIter + 1);
            //MultidimensionalArray ortho_Mxx = MultidimensionalArray.Create(L, MaxIter + 1);

            MultidimensionalArray MassMatrix = MultidimensionalArray.Create(MaxKrylovDim, MaxKrylovDim);
                
            int PCcounter = 0;
            for(int iIter = 0; iIter < MaxIter; iIter++) {
                m_ThisLevelIterations++;
                Debug.Assert(SolHistory.Count == MxxHistory.Count);
                Debug.Assert(SolHistory.Count == KrylovDim);

                // select preconditioner
                var Precond = PrecondS[PCcounter];
                PCcounter++;
                if(PCcounter >= PrecondS.Length)
                    PCcounter = 0;

                // solve the residual equation: M*Correction = prev. Residual
                PreviousRes.SetV(CurrentRes);
                Correction.ClearEntries();
                Precond.Solve(Correction, PreviousRes);

                // compute M*Correction
                Mtx.SpMV(1.0, Correction, 0.0, Mxx);

                // orthonormalize the Mxx -- vector with respect to the previous ones.
                Debug.Assert(KrylovDim == MxxHistory.Count);
                Debug.Assert(KrylovDim == SolHistory.Count);

                //raw_Mxx.SetColumn(KrylovDim, Mxx);

                for(int i = 0; i < KrylovDim; i++) {
                    Debug.Assert(!object.ReferenceEquals(Mxx, MxxHistory[i]));
                    double beta = GenericBlas.InnerProd(Mxx, MxxHistory[i]).MPISum();
                    Mxx.AccV(-beta, MxxHistory[i]);
                    Correction.AccV(-beta, SolHistory[i]);
                }
                {
                    double gamma = 1.0 / GenericBlas.L2NormPow2(Mxx).MPISum().Sqrt();
                    Mxx.ScaleV(gamma);
                    Correction.ScaleV(gamma);
                }

                //
                for(int i = 0; i < KrylovDim; i++) {
                    MassMatrix[i, KrylovDim] = GenericBlas.InnerProd(Mxx, MxxHistory[i]).MPISum();
                }
                MassMatrix[KrylovDim, KrylovDim] = GenericBlas.L2NormPow2(Mxx).MPISum();
                //


                //ortho_Mxx.SetColumn(KrylovDim, Mxx);

                MxxHistory.Add(Mxx.CloneAs());
                SolHistory.Add(Correction.CloneAs());
                KrylovDim++;

                // RHS of the minimization problem (LHS is identity matrix)
                _R.Add(GenericBlas.InnerProd(MxxHistory.Last(), Residual0).MPISum());

                // compute accelerated solution
                //double[] alpha = _R.ToArray(); // factors for re-combining solutions
                double[] alpha;
                {
                    double[] minimi_rhs = _R.ToArray();
                    var minimi_lhs = MassMatrix.ExtractSubArrayShallow(new int[] { 0, 0 }, new int[] { KrylovDim - 1, KrylovDim - 1 }).CloneAs();
                    alpha = new double[KrylovDim];
                    minimi_lhs.Solve(alpha, minimi_rhs);
                }

                Debug.Assert(alpha.Length == SolHistory.Count);
                Debug.Assert(alpha.Length == MxxHistory.Count);
                Debug.Assert(alpha.Length == KrylovDim);
                CurrentSol.SetV(Solution0, 1.0);
                for(int i = 0; i < KrylovDim; i++)
                    CurrentSol.AccV(alpha[i], SolHistory[i]);

                // compute new Residual
                CurrentRes.SetV(B);
                Mtx.SpMV(-1.0, CurrentSol, 1.0, CurrentRes);
                double crL2 = CurrentRes.L2Norm();                

                // diagnostic output
                if(this.IterationCallback != null)
                    this.IterationCallback(iIter + 1, CurrentSol.CloneAs(), CurrentRes.CloneAs(), this.m_mgop);

                if (crL2 < Tolerance) {
                    m_Converged = true;
                    break;
                }

                if(KrylovDim >= MaxKrylovDim) {
                    if(this.Restarted) {
                        // restarted version of the algorithm
                        // ++++++++++++++++++++++++++++++++++

                        MxxHistory.Clear();
                        SolHistory.Clear();
                        _R.Clear();
                        KrylovDim = 0;
                        Residual0.SetV(CurrentRes);
                        Solution0.SetV(CurrentSol);
                    } else {
                        // throw-away version of the algorithm
                        // +++++++++++++++++++++++++++++++++++

                        int i_leastSig = alpha.IndexOfMin(x => x.Abs());
                        MxxHistory.RemoveAt(i_leastSig);
                        SolHistory.RemoveAt(i_leastSig);
                        KrylovDim--;

                        for(int i = i_leastSig; i < KrylovDim; i++) {
                            for(int j = 0; j <= KrylovDim; j++) {
                                MassMatrix[i, j] = MassMatrix[i + 1, j];
                            }
                        }
                        for(int i = i_leastSig; i < KrylovDim; i++) {
                            for(int j = 0; j <= KrylovDim; j++) {
                                MassMatrix[j, i] = MassMatrix[j, i + 1];
                            }
                        }

                        Residual0.SetV(CurrentRes);
                        Solution0.SetV(CurrentSol);

                        _R.Clear();
                        foreach(double[] mxx in MxxHistory) {
                            _R.Add(GenericBlas.InnerProd(mxx, Residual0).MPISum());
                        }
                    }
                }
            }


            X.SetV(CurrentSol, 1.0);
            //raw_Mxx.SaveToTextFile("C:\\temp\\raw_Mxx.txt");
            //ortho_Mxx.SaveToTextFile("C:\\temp\\ortho_Mxx.txt");
        }


        /// <summary>
        /// If true, the orthonormalization is restarted when the maximum Krylov dimension is reached;
        /// Otherwise, Krylov dimension prevented to grow by just removing the least significant solution vector.
        /// </summary>
        public bool Restarted {
            get;
            set;
        }
        
        /// <summary>
        /// Maximum number of FlexGMRES iterations.
        /// </summary>
        public int MaxIter = 100;

        public double Tolerance = 1E-10;

        public int MaxKrylovDim = 80;



        bool m_Converged = false;
        int m_ThisLevelIterations = 0;

        public int IterationsInNested {
            get {
                if(this.PrecondS != null)
                    return this.PrecondS.Sum(pc => pc.IterationsInNested + pc.ThisLevelIterations);
                else
                    return 0;
            }
        }

        public int ThisLevelIterations {
            get { return this.m_ThisLevelIterations; }
        }

        public bool Converged {
            get { return this.m_Converged; }
        }

        public void ResetStat() {
            m_Converged = false;
            m_ThisLevelIterations = 0;
        }

        public Action<int, double[], double[], MultigridOperator> IterationCallback {
            get;
            set;
        }
    }
}
