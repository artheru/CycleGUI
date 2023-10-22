/*
  Native File Dialog

  User API

  http://www.frogtoss.com/labs
 */


#ifdef _WIN32 // For Windows
#define LIBVRENDER_EXPORT __declspec(dllexport)
#else // For Linux and other platforms
#define LIBVRENDER_EXPORT
#endif

#ifndef _NFD_H
#define _NFD_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stddef.h>

/* denotes UTF-8 char */
typedef char nfdchar_t;

/* opaque data structure -- see NFD_PathSet_* */
typedef struct {
    nfdchar_t *buf;
    size_t *indices; /* byte offsets into buf */
    size_t count;    /* number of indices into buf */
}nfdpathset_t;

typedef enum {
    NFD_ERROR,       /* programmatic error */
    NFD_OKAY,        /* user pressed okay, or successful return */
    NFD_CANCEL       /* user pressed cancel */
}nfdresult_t;
    

/* nfd_<targetplatform>.c */

/* single file open dialog */    
LIBVRENDER_EXPORT nfdresult_t NFD_OpenDialog( const nfdchar_t *filterList,
                            const nfdchar_t *defaultPath,
                            nfdchar_t **outPath );

/* multiple file open dialog */    
LIBVRENDER_EXPORT nfdresult_t NFD_OpenDialogMultiple( const nfdchar_t *filterList,
                                    const nfdchar_t *defaultPath,
                                    nfdpathset_t *outPaths );

/* save dialog */
LIBVRENDER_EXPORT nfdresult_t NFD_SaveDialog( const nfdchar_t *filterList,
                            const nfdchar_t *defaultPath,
                            nfdchar_t **outPath );


/* select folder dialog */
LIBVRENDER_EXPORT nfdresult_t NFD_PickFolder( const nfdchar_t *defaultPath,
                            nfdchar_t **outPath);

/* nfd_common.c */

/* get last error -- set when nfdresult_t returns NFD_ERROR */
LIBVRENDER_EXPORT const char *NFD_GetError( void );
/* get the number of entries stored in pathSet */
LIBVRENDER_EXPORT size_t      NFD_PathSet_GetCount( const nfdpathset_t *pathSet );
/* Get the UTF-8 path at offset index */
LIBVRENDER_EXPORT nfdchar_t  *NFD_PathSet_GetPath( const nfdpathset_t *pathSet, size_t index );
/* Free the pathSet */    
LIBVRENDER_EXPORT void        NFD_PathSet_Free( nfdpathset_t *pathSet );

LIBVRENDER_EXPORT void        NFD_Free(nfdchar_t** outPath);

#ifdef __cplusplus
}
#endif

#endif


#ifdef _WIN32
#include <windows.h>
extern HWND nfd_owner;
#endif