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

using BoSSS.Foundation.Grid;
using BoSSS.Platform.Utils.Geom;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BoSSS.Platform;
using ilPSP.Utils;
using BoSSS.Foundation;
using ilPSP.LinSolvers;
using ilPSP;
using ilPSP.Tracing;
using MPI.Wrappers;
using BoSSS.Foundation.Grid.Classic;

namespace BoSSS.Foundation.Grid.Aggregation {

    static public class CoarseningAlgorithms {

        /// <summary>
        /// Creates a sequence of aggregated grids, suitable for a multigrid algorithm
        /// </summary>
        /// <param name="GridDat">original grid</param>
        /// <param name="MaxDepth">maximum number of refinements</param>
        /// <returns></returns>
        public static AggregationGridData[] CreateSequence(IGridData GridDat, int MaxDepth = -1) {
            using(new FuncTrace()) {
                int D = GridDat.SpatialDimension;
                MaxDepth = MaxDepth >= 0 ? MaxDepth : int.MaxValue;
                //int cutoff = MaxDepth < 0 ? int.MaxValue : MaxDepth * skip;


                // create sequence of aggregation multigrid grids and basises 
                // ==========================================================

                List<AggregationGridData> aggGrids = new List<AggregationGridData>();
                aggGrids.Add(ZeroAggregation(GridDat));
                while(true) {
                    if( aggGrids.Count >= MaxDepth)
                        break;

             
                    AggregationGridData grid = Coarsen(aggGrids.Last(), (int)(Math.Pow(2, D)));


                    if ((grid.iLogicalCells.NoOfLocalUpdatedCells.MPISum() >= aggGrids.Last().iLogicalCells.NoOfLocalUpdatedCells.MPISum()))
                        // no more refinement possible
                        break;
                    
                    aggGrids.Add(grid);


#if DEBUG
                    int iLevel = aggGrids.Count - 2; // index of fine level (finer == low index)
                    int JFine = aggGrids[iLevel].iLogicalCells.Count;
                    int JCoarse = aggGrids[iLevel + 1].iLogicalCells.Count;
                    Debug.Assert(aggGrids[iLevel + 1].iLogicalCells.Count == (aggGrids[iLevel + 1].iLogicalCells.NoOfLocalUpdatedCells + aggGrids[iLevel + 1].iLogicalCells.NoOfExternalCells));
                    
                    // test that the coarse grid has significantly less cells than the fine grid.
                    double dJfine = aggGrids[iLevel].iLogicalCells.NoOfLocalUpdatedCells;
                    double dJcoarse = aggGrids[iLevel + 1].iLogicalCells.NoOfLocalUpdatedCells;
                    if (JCoarse >= 10)
                        Debug.Assert(dJfine * 0.8 >= dJcoarse);

                    // test the coarse-to-fine map
                    bool[] testMarker = new bool[JFine];
                    int[][] C2F = aggGrids[iLevel + 1].jCellCoarse2jCellFine;
                    Debug.Assert(C2F.Length == JCoarse);
                    for(int jC = 0; jC < JCoarse; jC++) {
                        foreach(int jF in C2F[jC]) {
                            Debug.Assert(testMarker[jF] == false);
                            testMarker[jF] = true;
                        }
                    }
                    for(int jF = 0; jF < JFine; jF++) {
                        Debug.Assert(testMarker[jF] == true);
                    }

                    // test the fine-to-coarse mapping
                    int[] F2C = aggGrids[iLevel + 1].jCellFine2jCellCoarse;
                    Debug.Assert(F2C.Length == JFine);
                    for (int jF = 0; jF < JFine; jF++) {
                        Debug.Assert(C2F[F2C[jF]].Contains(jF));
                    }

#endif
                }


                // return
                return aggGrids.ToArray();
            }
        }

        /// <summary>
        /// creates an initial aggregated grid which is in fact equivalent to <paramref name="g"/>
        /// </summary>
        public static AggregationGridData ZeroAggregation(IGridData g) {
            //var Cls = g.Cells;
            int J = g.iLogicalCells.NoOfLocalUpdatedCells;
            int D = g.SpatialDimension;

            int[][] AggregateCells = new int[J][];
            for(int j = 0; j < J; j++) {
                AggregateCells[j] = new int[] { j };
            }

            AggregationGridData ret = new AggregationGridData(g, AggregateCells);
            
            return ret;
        }

        /// <summary>
        /// coarsens level <paramref name="ag"/> (aggregation of grid data objects)
        /// </summary>
        /// <param name="ag">
        /// input grid, which should be aggregated
        /// </param>
        /// <param name="AggCellCount">
        /// desired number of parts for each aggregate cell
        /// </param>
        public static AggregationGridData Coarsen(IGridData ag, int AggCellCount) {
            using(new FuncTrace()) {

                int[][] Coarsened_ComositeCells = AggregationKernel(ag, AggCellCount);
                return new AggregationGridData(ag, Coarsened_ComositeCells);
            }
        }

        /// <summary>
        /// coarsens level <paramref name="ag"/> (aggregation of grid objects)
        /// </summary>
        /// <param name="ag">
        /// input grid, which should be aggregated
        /// </param>
        /// <param name="AggCellCount">
        /// desired number of parts for each aggregate cell
        /// </param>
        public static AggregationGrid Coarsen(IGrid ag, int AggCellCount) {
            using(new FuncTrace()) {

                IGridData pGridData = ag.iGridData;
                int[][] Coarsened_ComositeCells = AggregationKernel(pGridData, AggCellCount);
                return new AggregationGrid(ag, Coarsened_ComositeCells);
            }
        }


        private static int[][] AggregationKernel(IGridData ag, int AggCellCount) {
            int Jloc = ag.iLogicalCells.NoOfLocalUpdatedCells;
            int D = ag.SpatialDimension;
            if (AggCellCount < 2)
                throw new ArgumentOutOfRangeException();


            // sort cells of parent grid by size:
            // we want to aggregate the smallest cells at first.
            int[] Perm = Jloc.ForLoop(j => j).OrderBy(j => ag.iLogicalCells.GetCellVolume(j)).ToArray();

            BitArray UsedCellMarker = new BitArray(Jloc);

            List<int[]> Coarsened_ComositeCells = new List<int[]>();

            //
            List<int> aggCell = new List<int>();
            List<int> NeighCandidates = new List<int>();

            // loop over aggregated cells of parent grid...
            int[][] Neighbourship = ag.iLogicalCells.CellNeighbours;
            for (int i = 0; i < Jloc; i++) {
                int jCell = Perm[i]; // pick next cell
                Debug.Assert(Neighbourship[jCell].Contains(jCell) == false);
                if (!UsedCellMarker[jCell]) { // if the cell is not already agglomerated to another cell

                    aggCell.Clear();
                    aggCell.Add(jCell);
                    UsedCellMarker[jCell] = true;

                    for (int iPass = 1; iPass < AggCellCount; iPass++) {


                        // list of all neighbor cells which were not already aggregated to some other cell:
                        NeighCandidates.Clear();
                        foreach (int j in aggCell) {
                            Debug.Assert(j < Jloc);
                            Debug.Assert(UsedCellMarker[j] == true);
                            foreach (int jNeigh in Neighbourship[j]) {
                                if (jNeigh >= Jloc)
                                    continue;
                                if (UsedCellMarker[jNeigh] == true)
                                    continue;
                                Debug.Assert(aggCell.Contains(jNeigh) == false); // for all cells which are already in 'aggCell', the marker should be true
                                if (!NeighCandidates.Contains(jNeigh))
                                    NeighCandidates.Add(jNeigh);
                            }
                        }


                        int NN = NeighCandidates.Count;

                        if (NN > 0) {
                            double[] sizes = new double[NN];
                            BoundingBox[] aggBB = new BoundingBox[NN];
                            double[] aggBBaspect = new double[NN];
                            for (int iNeig = 0; iNeig < NN; iNeig++) { // loop over all candidates...
                                int jCellNeigh = NeighCandidates[iNeig];

                                aggBB[iNeig] = new BoundingBox(D); //ag.CompositeCellBB[jCell].CloneAs();
                                foreach (int jTaken in aggCell) {
                                    BoundingBox TempBB = new BoundingBox(D);
                                    ag.iLogicalCells.GetCellBoundingBox(jTaken, TempBB);
                                    aggBB[iNeig].AddBB(TempBB);
                                }

                                BoundingBox NeighBB = new BoundingBox(D);
                                ag.iLogicalCells.GetCellBoundingBox(jCellNeigh, NeighBB);

                                aggBB[iNeig].AddBB(NeighBB);
                                sizes[iNeig] = ag.iLogicalCells.GetCellVolume(jCell) + ag.iLogicalCells.GetCellVolume(jCellNeigh);
                                aggBBaspect[iNeig] = aggBB[iNeig].AspectRatio;
                            }

                            double[] RelSizes = sizes.CloneAs(); RelSizes.ScaleV(1.0 / sizes.Max());
                            double[] RelAspects = aggBBaspect.CloneAs(); RelAspects.ScaleV(1.0 / aggBBaspect.Max());

                            double[] Quality = new double[NN];
                            Quality.AccV(0.7, RelSizes);
                            Quality.AccV(0.3, RelAspects);

                            int iChoice = Quality.IndexOfMin(q => q);
                            int jNeigChoice = NeighCandidates[iChoice];

                            Debug.Assert(aggCell.Contains(jNeigChoice) == false);
                            aggCell.Add(jNeigChoice);
                            UsedCellMarker[jNeigChoice] = true;
                        } else {
                            // No neighbour available => unable to coarsen
                            break;
                        }
                    }

                    // add agglom cell
                    {
                        int[] aggCell_Fix = aggCell.ToArray();
#if DEBUG
                        foreach (int j in aggCell_Fix) {
                            Debug.Assert(j >= 0);
                            Debug.Assert(j < Jloc);
                            Debug.Assert(UsedCellMarker[j] == true);
                        }

#endif
                        Coarsened_ComositeCells.Add(aggCell_Fix);
                    }
                } else {
                    // cell already done.
                    continue;
                }
            }
            Debug.Assert(UsedCellMarker.ToBoolArray().Where(b => !b).Count() == 0, "some cell was not processed.");
            return Coarsened_ComositeCells.ToArray();
        }


        static bool IsAnc(IGridData gdat, AggregationGridData aGdat) {
            if (object.ReferenceEquals(aGdat.ParentGrid, gdat))
                return true;

            if (aGdat.ParentGrid is AggregationGridData)
                return IsAnc(gdat, (AggregationGridData)(aGdat.ParentGrid));

            return false;
        }


        /// <summary>
        /// assigns the cell index of the aggregated cell <em>j</em> to all (fine) grid cells that 
        /// the aggregated cell <em>j</em> consists of.
        /// </summary>
        static public void ColorDGField(this AggregationGridData ag, DGField f) {
            IGridData Anc = f.GridDat;
            if (!IsAnc(Anc, ag))
                throw new ArgumentException("Field 'f' must be defined on an ancestor grid of 'ag'.");


            f.Clear();
            int Jag = ag.iLogicalCells.NoOfLocalUpdatedCells;
            int Janc = Anc.iLogicalCells.NoOfLocalUpdatedCells;

            Debug.Assert(Anc.iGeomCells.Count == ag.iGeomCells.Count);
           
            int[] jG2jL = Anc.iGeomCells.GeomCell2LogicalCell;

            int[] Colors = new int[Jag];
            BitArray Marked = new BitArray(Janc);
            for (int j = 0; j < Jag; j++) { // loop over logical/aggregate cells

                // determine colors of neighbor cells
                int[] Neighs = ag.iLogicalCells.CellNeighbours[j];
                var NeighColors = Neighs.Select(jN => Colors[jN]);

                // select color for cell 'j'
                int iCol = 1;
                for(iCol = 1; iCol < 2 * Jag; iCol++) {
                    if(!NeighColors.Contains(iCol))
                        break;
                }
                Colors[j] = iCol;

                // color all logical cells in ancestor grid
                // idea: convert to geometrical and back to logical
                //    in this way we can e.g. skip multiple grid levels
                foreach (int jGeom in ag.iLogicalCells.AggregateCellToParts[j]) {
                    int jLogAnc;
                    if (jG2jL != null) {
                        jLogAnc = jG2jL[jGeom];
                    } else {
                        jLogAnc = jGeom;
                    }
                    if (!Marked[jLogAnc]) {
                        f.SetMeanValue(jLogAnc, iCol);
                        Marked[jLogAnc] = true;
                    } else {
                        // nop
                    }
                }
            }
        }
        
        

            /*

        /// <summary>
        /// Mapping from composite/aggegated cells to base grid cells.<br/>
        /// 1st index: composite/aggregated cell index <em>j</em> <br/>
        /// 2nd index: list of cells which from the aggregated cell <em>j</em>. <br/>
        /// </summary>
        public int[][] AggregateCells;


        int[] m_jCellToAggCell = null;
        int[] m_jCellToAggk = null;

        /// <summary>
        /// mapping from base grid cells to composite cells<br/>
        /// index: local cell index.
        /// </summary>
        public int[] jCellToAggCell {
            get {
                InitInverseMapping();
                return m_jCellToAggCell;
            }
        }

        /// <summary>
        /// mapping from base grid cells to cell index within aggregate cells<br/>
        /// index: local cell index.
        /// </summary>
        public int[] jCellToAggk {
            get {
                InitInverseMapping();
                return m_jCellToAggk;
            }
        }

        private void InitInverseMapping() {
            if(m_jCellToAggCell == null) {
                m_jCellToAggCell = new int[this.GridDat.Cells.NoOfLocalUpdatedCells];
                m_jCellToAggk = new int[this.GridDat.Cells.NoOfLocalUpdatedCells];
#if DEBUG
                m_jCellToAggCell.SetAll(-1234);
                m_jCellToAggk.SetAll(-123450);

#endif
                int JAGG = this.AggregateCells.Length;
                for(int jagg = 0; jagg < JAGG; jagg++) {
                    int[] AggCell = AggregateCells[jagg];

                    for(int k = AggCell.Length - 1; k >= 0; k--) {
                        int jCell = AggCell[k];
                        Debug.Assert(m_jCellToAggCell[jCell] < 0);
                        m_jCellToAggCell[jCell] = jagg;
                        m_jCellToAggk[jCell] = k;
                    }
                }
#if DEBUG
                foreach(int jagg in m_jCellToAggCell) {
                    Debug.Assert(jagg >= 0 && jagg < JAGG);
                }

                for(int j = 0; j < m_jCellToAggCell.Length; j++) {
                    int jAgg = m_jCellToAggCell[j];
                    int k = m_jCellToAggk[j];
                    Debug.Assert(this.AggregateCells[jAgg][k] == j);
                }
#endif
            }
        }


        /// <summary>
        /// Bounding boxes of the composite/aggregated cells.
        /// 
        /// Index: composite/aggregated cell index, correlates with first index of <see cref="AggregateCells"/>.
        /// </summary>
        public BoundingBox[] CompositeCellBB;


        */


    }
}
