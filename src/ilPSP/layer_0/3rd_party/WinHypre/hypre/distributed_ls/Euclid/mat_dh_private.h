/*BHEADER**********************************************************************
 * Copyright (c) 2008,  Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * This file is part of HYPRE.  See file COPYRIGHT for details.
 *
 * HYPRE is free software; you can redistribute it and/or modify it under the
 * terms of the GNU Lesser General Public License (as published by the Free
 * Software Foundation) version 2.1 dated February 1999.
 *
 * $Revision: 2.6 $
 ***********************************************************************EHEADER*/




#ifndef MAT_DH_PRIVATE
#define MAT_DH_PRIVATE

/* Functions called by Mat_dh, Factor_dh, and possibly others.
   Also, a few handy functions for dealing with permutations,
   etc.
 
 */

#include "euclid_common.h"

extern int mat_find_owner(int *beg_rows, int *end_rows, int index);

extern void mat_dh_transpose_private(int m, int *rpIN, int **rpOUT,
                                     int *cvalIN, int **cvalOUT,
                                     double *avalIN, double **avalOUT);

  /* same as above, but memory for output was already allocated */
extern void mat_dh_transpose_reuse_private(int m, 
                                     int *rpIN, int *cvalIN, double *avalIN,
                                     int *rpOUT, int *cvalOUT, double *avalOUT);

/*-------------------------------------------------------------------------
 * utility functions for reading and writing matrices in various formats.
 * currently recognized filetypes (formats) are:
 *    trip
 *    csr
 *    petsc
 * the "ignore" parameter is only used for the matrix "trip" format,
 * and the vector "csr" and "trip" formats (which are misnamed, and identical);
 * the intention is to skip over the first "ignore" lines of the file;
 * this is a hack to enable reading of Matrix Market, etc, formats. 
 *-------------------------------------------------------------------------*/
extern void readMat(Mat_dh *Aout, char *fileType, char *fileName, int ignore);
extern void readVec(Vec_dh *bout, char *fileType, char *fileName, int ignore);
extern void writeMat(Mat_dh Ain, char *fileType, char *fileName);
extern void writeVec(Vec_dh b, char *fileType, char *fileName);

/* Next function is primarily (?) for testing/development/debugging.
   P_0 reads and partitions the matrix, then distributes 
   amongst the other processors.
*/
extern void readMat_par(Mat_dh *Aout, char *fileType, char *fileName, int ignore);

extern void profileMat(Mat_dh A);
  /* writes structural and numerical symmetry and other info to stdout;
     for a single mpi task only.
  */



/*-------------------------------------------------------------------------*
 * functions called by public Mat_dh class methods.
 *
 *   (following notes need to be updated!)
 *
 *         m is number of local rows;
 *         beg_row is global number of 1st locally owned row;
 *         m, beg_row, rp, cval may not be null (caller's responsiblity);
 *         if n2o is NULL, it's assumed that o2n is NULL;
 *         if 
 *
 *         error thrown:
 *         if a nonlocal column (a column index that is less than beg_row,
 *         or >= beg_row+m), and can't be located in hash table.
 *
 *         print_triples_private() and print_mat_private() are 1-based.
 *
 *-------------------------------------------------------------------------*/

/* seq or mpi */
extern void mat_dh_print_graph_private(int m, int beg_row, int *rp, int *cval, 
                   double *aval, int *n2o, int *o2n, Hash_i_dh hash, FILE* fp);


/* seq; reordering not implemented */
/* see io_dh.h
                                int *rp, int *cval, double *aval, 
                           int *n2o, int *o2n, Hash_i_dh hash, char *filename);
*/

/* seq only */
extern void mat_dh_print_csr_private(int m, int *rp, int *cval, double *aval,
                                                                    FILE* fp); 


/* seq only */
extern void mat_dh_read_csr_private(int *m, int **rp, int **cval, double **aval,
                                                                    FILE* fp); 

/* seq only */
extern void mat_dh_read_triples_private(int ignore, int *m, int **rp, 
                                         int **cval, double **aval, FILE* fp); 

/* seq or mpi */ 
/* see io_dh.h
                                     double **aval, char *filename);
*/

/*-------------------------------------------------------------------------*/

extern void create_nat_ordering_private(int m, int **p);
extern void destroy_nat_ordering_private(int *p);
extern void invert_perm(int m, int *pIN, int *pOUT);


extern void make_full_private(int m, int **rp, int **cval, double **aval);
  /* converts upper or lower triangular to full;
     may bomb if input is not triangular!
   */

extern void make_symmetric_private(int m, int **rp, int **cval, double **aval);
  /* pads with zeros to make structurally symmetric. */

extern void make_symmetric_private(int m, int **rp, int **cval, double **aval);

#endif
