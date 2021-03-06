﻿using BoSSS.Platform.LinAlg;
using BoSSS.Solution.Gnuplot;
using ilPSP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoSSS.Application.SipPoisson.Voronoi {

    /// <summary>
    /// polygon tesselation/triangulating algorithms, 
    /// used reduce 2D Voronoi meshes to triangular meshes.
    /// </summary>
    static class PolygonTesselation {

        static double sign(Vector p1, Vector p2, Vector p3) {
            if (p1.Dim != 2)
                throw new ArgumentException();
            if (p2.Dim != 2)
                throw new ArgumentException();
            if (p3.Dim != 2)
                throw new ArgumentException();

            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        static bool PointInTriangle(Vector pt, Vector v1, Vector v2, Vector v3) {


            double d1, d2, d3;
            bool has_neg, has_pos;

            d1 = sign(pt, v1, v2);
            d2 = sign(pt, v2, v3);
            d3 = sign(pt, v3, v1);

            has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }


        static bool IsEar(List<Vector> Polygon, int iTst) {
            int L = Polygon.Count;

            if (iTst < 0)
                throw new IndexOutOfRangeException();
            if (iTst >= L)
                throw new IndexOutOfRangeException();

            int iNxt = iTst + 1;
            if (iNxt >= L)
                iNxt -= L;
            int iPrv = iTst - 1;
            if (iPrv < 0)
                iPrv += L;

            Vector vPrv = Polygon[iPrv];
            Vector vTst = Polygon[iTst];
            Vector vNxt = Polygon[iNxt];


            for(int l = 0; l < L; l++) {
                if (l == iPrv || l == iNxt || l == iTst)
                    continue;

                if (PointInTriangle(Polygon[l], vPrv, vNxt, vTst))
                    return false;
            }

            Vector D1 = vPrv - vTst;
            Vector D2 = vNxt - vTst;
            if (D1.AngleTo(D2) >= (179.9999999 / 180.0) * Math.PI) {
                //if (D1.CrossProduct2D(D2).Abs() < 1.0e-6)
                D1.AngleTo(D2);
                return false; // this is a hack to avoid very skinny triangles
            }
            return true;
        }
    
        public static int[,] TesselatePolygon(IEnumerable<Vector> _Polygon) {
            List<Vector> Polygon = _Polygon.ToList();
            List<int> orgIdx = Polygon.Count.ForLoop(i => i).ToList();

            //var R = new List<ValueTuple<int, int, int>>();
            int[,] R = new int[Polygon.Count - 2, 3];


            int I = Polygon.Count - 2;
            for(int i = 0; i < I; i++) { // Theorem: every simple polygon decomposes into (I-2) triangles

                int iEar = -1;
                for (int iTst = 0; iTst < Polygon.Count; iTst++) {
                    if(IsEar(Polygon, iTst)) {
                        iEar = iTst;
                        break;
                    }
                }

                if (iEar < 0) {
                    /*
                    using(var gp = new Gnuplot()) {
                        double[] xn = Polygon.Select(X => X.x).ToArray();
                        double[] yn = Polygon.Select(X => X.y).ToArray();
                        double[] xm = _Polygon.Select(X => X.x).ToArray();
                        double[] ym = _Polygon.Select(X => X.y).ToArray();

                        gp.PlotXY(xn, yn, "earless", new PlotFormat("-xb"));
                        gp.PlotXY(xm, ym, "orginal", new PlotFormat(":0r"));

                        gp.Execute();

                        Console.ReadKey();
                    }
                    */
                    throw new ArithmeticException("unable to find ear.");
                }
                
                int iNxt = iEar + 1;
                if (iNxt >= Polygon.Count)
                    iNxt -= Polygon.Count;
                int iPrv = iEar - 1;
                if (iPrv < 0)
                    iPrv += Polygon.Count;

                R[i, 0] = orgIdx[iPrv];
                R[i, 1] = orgIdx[iEar];
                R[i, 2] = orgIdx[iNxt];

                orgIdx.RemoveAt(iEar);
                Polygon.RemoveAt(iEar);
            }

            // return
            return R;
        }



        public static int[,] TesselateConvexPolygon(IEnumerable<Vector> _Polygon) {
            Vector[] Polygon = _Polygon.ToArray();
            int NV = _Polygon.Count();
            int[,] R = new int[NV - 2, 3];

            for (int iTri = 0; iTri < NV - 2; iTri++) { // loop over triangles of voronoi cell
                int iV0 = 0;
                int iV1 = iTri + 1;
                int iV2 = iTri + 2;
                R[iTri, 0] = iV0;
                R[iTri, 1] = iV1;
                R[iTri, 2] = iV2;

                /*
                Vector V0 = Polygon[iV0];
                Vector V1 = Polygon[iV1];
                Vector V2 = Polygon[iV2];

                Vector D1 = V1 - V0;
                Vector D2 = V2 - V0;
                if(!(D1.CrossProduct2D(D2) > 1.0e-8)) {
                    throw new ArithmeticException("Not a convex polygon.");
                }
                //if (D1.CrossProduct2D(D2) < 0) {
                //    Vector T = V2;
                //    int t = iV2;
                //    V2 = V1;
                //    iV2 = iV1;
                //    V1 = T;
                //    iV1 = t;
                //}
                */
            }

            return R;
        }



        
    }
}
