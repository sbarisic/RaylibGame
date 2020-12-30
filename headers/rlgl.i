
/**********************************************************************************************
*
*   rlgl - raylib OpenGL abstraction layer
*
*   rlgl is a wrapper for multiple OpenGL versions (1.1, 2.1, 3.3 Core, ES 2.0) to
*   pseudo-OpenGL 1.1 style functions (rlVertex, rlTranslate, rlRotate...).
*
*   When chosing an OpenGL version greater than OpenGL 1.1, rlgl stores vertex data on internal
*   VBO buffers (and VAOs if available). It requires calling 3 functions:
*       rlglInit()  - Initialize internal buffers and auxiliar resources
*       rlglDraw()  - Process internal buffers and send required draw calls
*       rlglClose() - De-initialize internal buffers data and other auxiliar resources
*
*   CONFIGURATION:
*
*   #define GRAPHICS_API_OPENGL_11
*   #define GRAPHICS_API_OPENGL_21
*   #define GRAPHICS_API_OPENGL_33
*   #define GRAPHICS_API_OPENGL_ES2
*       Use selected OpenGL graphics backend, should be supported by platform
*       Those preprocessor defines are only used on rlgl module, if OpenGL version is
*       required by any other module, use rlGetVersion() tocheck it
*
*   #define RLGL_IMPLEMENTATION
*       Generates the implementation of the library into the included file.
*       If not defined, the library is in header only mode and can be included in other headers
*       or source files without problems. But only ONE file should hold the implementation.
*
*   #define RLGL_STANDALONE
*       Use rlgl as standalone library (no raylib dependency)
*
*   #define SUPPORT_VR_SIMULATOR
*       Support VR simulation functionality (stereo rendering)
*
*   DEPENDENCIES:
*       raymath     - 3D math functionality (Vector3, Matrix, Quaternion)
*       GLAD        - OpenGL extensions loading (OpenGL 3.3 Core only)
*
*
*   LICENSE: zlib/libpng
*
*   Copyright (c) 2014-2019 Ramon Santamaria (@raysan5)
*
*   This software is provided "as-is", without any express or implied warranty. In no event
*   will the authors be held liable for any damages arising from the use of this software.
*
*   Permission is granted to anyone to use this software for any purpose, including commercial
*   applications, and to alter it and redistribute it freely, subject to the following restrictions:
*
*     1. The origin of this software must not be misrepresented; you must not claim that you
*     wrote the original software. If you use this software in a product, an acknowledgment
*     in the product documentation would be appreciated but is not required.
*
*     2. Altered source versions must be plainly marked as such, and must not be misrepresented
*     as being the original software.
*
*     3. This notice may not be removed or altered from any source distribution.
*
**********************************************************************************************/



























    
/**********************************************************************************************
*
*   raylib - A simple and easy-to-use library to enjoy videogames programming (www.raylib.com)
*
*   FEATURES:
*       - NO external dependencies, all required libraries included with raylib
*       - Multiplatform: Windows, Linux, FreeBSD, OpenBSD, NetBSD, DragonFly, MacOS, UWP, Android, Raspberry Pi, HTML5.
*       - Written in plain C code (C99) in PascalCase/camelCase notation
*       - Hardware accelerated with OpenGL (1.1, 2.1, 3.3 or ES2 - choose at compile)
*       - Unique OpenGL abstraction layer (usable as standalone module): [rlgl]
*       - Powerful fonts module (XNA SpriteFonts, BMFonts, TTF)
*       - Outstanding texture formats support, including compressed formats (DXT, ETC, ASTC)
*       - Full 3d support for 3d Shapes, Models, Billboards, Heightmaps and more!
*       - Flexible Materials system, supporting classic maps and PBR maps
*       - Skeletal Animation support (CPU bones-based animation)
*       - Shaders support, including Model shaders and Postprocessing shaders
*       - Powerful math module for Vector, Matrix and Quaternion operations: [raymath]
*       - Audio loading and playing with streaming support (WAV, OGG, MP3, FLAC, XM, MOD)
*       - VR stereo rendering with configurable HMD device parameters
*       - Bindings to multiple programming languages available!
*
*   NOTES:
*       One custom font is loaded by default when InitWindow() [core]
*       If using OpenGL 3.3 or ES2, one default shader is loaded automatically (internally defined) [rlgl]
*       If using OpenGL 3.3 or ES2, several vertex buffers (VAO/VBO) are created to manage lines-triangles-quads
*
*   DEPENDENCIES (included):
*       [core] rglfw (github.com/glfw/glfw) for window/context management and input (only PLATFORM_DESKTOP)
*       [rlgl] glad (github.com/Dav1dde/glad) for OpenGL 3.3 extensions loading (only PLATFORM_DESKTOP)
*       [raudio] miniaudio (github.com/dr-soft/miniaudio) for audio device/context management
*
*   OPTIONAL DEPENDENCIES (included):
*       [core] rgif (Charlie Tangora, Ramon Santamaria) for GIF recording
*       [textures] stb_image (Sean Barret) for images loading (BMP, TGA, PNG, JPEG, HDR...)
*       [textures] stb_image_write (Sean Barret) for image writting (BMP, TGA, PNG, JPG)
*       [textures] stb_image_resize (Sean Barret) for image resizing algorithms
*       [textures] stb_perlin (Sean Barret) for Perlin noise image generation
*       [text] stb_truetype (Sean Barret) for ttf fonts loading
*       [text] stb_rect_pack (Sean Barret) for rectangles packing
*       [models] par_shapes (Philip Rideout) for parametric 3d shapes generation
*       [models] tinyobj_loader_c (Syoyo Fujita) for models loading (OBJ, MTL)
*       [models] cgltf (Johannes Kuhlmann) for models loading (glTF)
*       [raudio] stb_vorbis (Sean Barret) for OGG audio loading
*       [raudio] dr_flac (David Reid) for FLAC audio file loading
*       [raudio] dr_mp3 (David Reid) for MP3 audio file loading
*       [raudio] jar_xm (Joshua Reisenauer) for XM audio module loading
*       [raudio] jar_mod (Joshua Reisenauer) for MOD audio module loading
*
*
*   LICENSE: zlib/libpng
*
*   raylib is licensed under an unmodified zlib/libpng license, which is an OSI-certified,
*   BSD-like license that allows static linking with closed source software:
*
*   Copyright (c) 2013-2019 Ramon Santamaria (@raysan5)
*
*   This software is provided "as-is", without any express or implied warranty. In no event
*   will the authors be held liable for any damages arising from the use of this software.
*
*   Permission is granted to anyone to use this software for any purpose, including commercial
*   applications, and to alter it and redistribute it freely, subject to the following restrictions:
*
*     1. The origin of this software must not be misrepresented; you must not claim that you
*     wrote the original software. If you use this software in a product, an acknowledgment
*     in the product documentation would be appreciated but is not required.
*
*     2. Altered source versions must be plainly marked as such, and must not be misrepresented
*     as being the original software.
*
*     3. This notice may not be removed or altered from any source distribution.
*
**********************************************************************************************/





//
// stdarg.h
//
//      Copyright (c) Microsoft Corporation. All rights reserved.
//
// The C Standard Library <stdarg.h> header.
//
#pragma once



//
// vcruntime.h
//
//      Copyright (c) Microsoft Corporation. All rights reserved.
//
// Declarations used throughout the VCRuntime library.
//
#pragma once
//
// Note on use of "deprecate":
//
// Various places in this header and other headers use
// __declspec(deprecate) or macros that have the term DEPRECATE in them.
// We use "deprecate" here ONLY to signal the compiler to emit a warning
// about these items. The use of "deprecate" should NOT be taken to imply
// that any standard committee has deprecated these functions from the
// relevant standards.  In fact, these functions are NOT deprecated from
// the standard.
//
// Full details can be found in our documentation by searching for
// "Security Enhancements in the CRT".
//




    


// The _CRTIMP macro is not used in the VCRuntime or the CoreCRT anymore, but
// there is a lot of existing code that declares CRT functions using this macro,
// and if we remove its definition, we break that existing code.  It is thus
// defined here only for compatibility.

    
    


        


            
        
    



/***
*sal.h - markers for documenting the semantics of APIs
*
*       Copyright (c) Microsoft Corporation. All rights reserved.
*
*Purpose:
*       sal.h provides a set of annotations to describe how a function uses its
*       parameters - the assumptions it makes about them, and the guarantees it makes
*       upon finishing.
*
*       [Public]
*
****/
#pragma once

/*==========================================================================

   The comments in this file are intended to give basic understanding of
   the usage of SAL, the Microsoft Source Code Annotation Language.
   For more details, please see https://go.microsoft.com/fwlink/?LinkID=242134

   The macros are defined in 3 layers, plus the structural set:

   _In_/_Out_/_Ret_ Layer:
   ----------------------
   This layer provides the highest abstraction and its macros should be used
   in most cases. These macros typically start with:
      _In_     : input parameter to a function, unmodified by called function
      _Out_    : output parameter, written to by called function, pointed-to
                 location not expected to be initialized prior to call
      _Outptr_ : like _Out_ when returned variable is a pointer type
                 (so param is pointer-to-pointer type). Called function
                 provides/allocated space.
      _Outref_ : like _Outptr_, except param is reference-to-pointer type.
      _Inout_  : inout parameter, read from and potentially modified by
                 called function.
      _Ret_    : for return values
      _Field_  : class/struct field invariants
   For common usage, this class of SAL provides the most concise annotations.
   Note that _In_/_Out_/_Inout_/_Outptr_ annotations are designed to be used
   with a parameter target. Using them with _At_ to specify non-parameter
   targets may yield unexpected results.

   This layer also includes a number of other properties that can be specified
   to extend the ability of code analysis, most notably:
      -- Designating parameters as format strings for printf/scanf/scanf_s
      -- Requesting stricter type checking for C enum parameters

   _Pre_/_Post_ Layer:
   ------------------
   The macros of this layer only should be used when there is no suitable macro
   in the _In_/_Out_ layer. Its macros start with _Pre_ or _Post_.
   This layer provides the most flexibility for annotations.

   Implementation Abstraction Layer:
   --------------------------------
   Macros from this layer should never be used directly. The layer only exists
   to hide the implementation of the annotation macros.

   Structural Layer:
   ----------------
   These annotations, like _At_ and _When_, are used with annotations from
   any of the other layers as modifiers, indicating exactly when and where
   the annotations apply.


   Common syntactic conventions:
   ----------------------------

   Usage:
   -----
   _In_, _Out_, _Inout_, _Pre_, _Post_, are for formal parameters.
   _Ret_, _Deref_ret_ must be used for return values.

   Nullness:
   --------
   If the parameter can be NULL as a precondition to the function, the
   annotation contains _opt. If the macro does not contain '_opt' the
   parameter cannot be NULL.

   If an out/inout parameter returns a null pointer as a postcondition, this is
   indicated by _Ret_maybenull_ or _result_maybenull_. If the macro is not
   of this form, then the result will not be NULL as a postcondition.
     _Outptr_ - output value is not NULL
     _Outptr_result_maybenull_ - output value might be NULL

   String Type:
   -----------
   _z: NullTerminated string
   for _In_ parameters the buffer must have the specified stringtype before the call
   for _Out_ parameters the buffer must have the specified stringtype after the call
   for _Inout_ parameters both conditions apply

   Extent Syntax:
   -------------
   Buffer sizes are expressed as element counts, unless the macro explicitly
   contains _byte_ or _bytes_. Some annotations specify two buffer sizes, in
   which case the second is used to indicate how much of the buffer is valid
   as a postcondition. This table outlines the precondition buffer allocation
   size, precondition number of valid elements, postcondition allocation size,
   and postcondition number of valid elements for representative buffer size
   annotations:
                                     Pre    |  Pre    |  Post   |  Post
                                     alloc  |  valid  |  alloc  |  valid
      Annotation                     elems  |  elems  |  elems  |  elems
      ----------                     ------------------------------------
      _In_reads_(s)                    s    |   s     |   s     |   s
      _Inout_updates_(s)               s    |   s     |   s     |   s
      _Inout_updates_to_(s,c)          s    |   s     |   s     |   c
      _Out_writes_(s)                  s    |   0     |   s     |   s
      _Out_writes_to_(s,c)             s    |   0     |   s     |   c
      _Outptr_result_buffer_(s)        ?    |   ?     |   s     |   s
      _Outptr_result_buffer_to_(s,c)   ?    |   ?     |   s     |   c

   For the _Outptr_ annotations, the buffer in question is at one level of
   dereference. The called function is responsible for supplying the buffer.

   Success and failure:
   -------------------
   The SAL concept of success allows functions to define expressions that can
   be tested by the caller, which if it evaluates to non-zero, indicates the
   function succeeded, which means that its postconditions are guaranteed to
   hold.  Otherwise, if the expression evaluates to zero, the function is
   considered to have failed, and the postconditions are not guaranteed.

   The success criteria can be specified with the _Success_(expr) annotation:
     _Success_(return != FALSE) BOOL
     PathCanonicalizeA(_Out_writes_(MAX_PATH) LPSTR pszBuf, LPCSTR pszPath) :
        pszBuf is only guaranteed to be NULL-terminated when TRUE is returned,
        and FALSE indiates failure. In common practice, callers check for zero
        vs. non-zero returns, so it is preferable to express the success
        criteria in terms of zero/non-zero, not checked for exactly TRUE.

   Functions can specify that some postconditions will still hold, even when
   the function fails, using _On_failure_(anno-list), or postconditions that
   hold regardless of success or failure using _Always_(anno-list).

   The annotation _Return_type_success_(expr) may be used with a typedef to
   give a default _Success_ criteria to all functions returning that type.
   This is the case for common Windows API status types, including
   HRESULT and NTSTATUS.  This may be overridden on a per-function basis by
   specifying a _Success_ annotation locally.

============================================================================*/



































// Disable expansion of SAL macros in non-Prefast mode to
// improve compiler throughput.









// safeguard for MIDL and RC builds

































// Some annotations aren't officially SAL2 yet.





//============================================================================
//   Structural SAL:
//     These annotations modify the use of other annotations.  They may
//     express the annotation target (i.e. what parameter/field the annotation
//     applies to) or the condition under which the annotation is applicable.
//============================================================================

// _At_(target, annos) specifies that the annotations listed in 'annos' is to
// be applied to 'target' rather than to the identifier which is the current
// lexical target.


// _At_buffer_(target, iter, bound, annos) is similar to _At_, except that
// target names a buffer, and each annotation in annos is applied to each
// element of target up to bound, with the variable named in iter usable
// by the annotations to refer to relevant offsets within target.


// _When_(expr, annos) specifies that the annotations listed in 'annos' only
// apply when 'expr' evaluates to non-zero.




// <expr> indicates whether normal post conditions apply to a function


// <expr> indicates whether post conditions apply to a function returning
// the type that this annotation is applied to


// Establish postconditions that apply only if the function does not succeed


// Establish postconditions that apply in both success and failure cases.
// Only applicable with functions that have  _Success_ or _Return_type_succss_.


// Usable on a function defintion. Asserts that a function declaration is
// in scope, and its annotations are to be used. There are no other annotations
// allowed on the function definition.


// _Notref_ may precede a _Deref_ or "real" annotation, and removes one
// level of dereference if the parameter is a C++ reference (&).  If the
// net deref on a "real" annotation is negative, it is simply discarded.


// Annotations for defensive programming styles.







//============================================================================
//   _In_/_Out_ Layer:
//============================================================================

// Reserved pointer parameters, must always be NULL.


// _Const_ allows specification that any namable memory location is considered
// readonly for a given call.



// Input parameters --------------------------

//   _In_ - Annotations for parameters where data is passed into the function, but not modified.
//          _In_ by itself can be used with non-pointer types (although it is redundant).

// e.g. void SetPoint( _In_ const POINT* pPT );



// nullterminated 'in' parameters.
// e.g. void CopyStr( _In_z_ const char* szFrom, _Out_z_cap_(cchTo) char* szTo, size_t cchTo );




// 'input' buffers with given size











// 'input' buffers valid to the given end pointer








// Output parameters --------------------------

//   _Out_ - Annotations for pointer or reference parameters where data passed back to the caller.
//           These are mostly used where the pointer/reference is to a non-pointer type.
//           _Outptr_/_Outref) (see below) are typically used to return pointers via parameters.

// e.g. void GetPoint( _Out_ POINT* pPT );


























// Inout parameters ----------------------------

//   _Inout_ - Annotations for pointer or reference parameters where data is passed in and
//        potentially modified.
//          void ModifyPoint( _Inout_ POINT* pPT );
//          void ModifyPointByRef( _Inout_ POINT& pPT );




// For modifying string buffers
//   void toupper( _Inout_z_ char* sz );



// For modifying buffers with explicit element size











// For modifying buffers with explicit byte size










// Pointer to pointer parameters -------------------------

//   _Outptr_ - Annotations for output params returning pointers
//      These describe parameters where the called function provides the buffer:
//        HRESULT SHStrDupW(_In_ LPCWSTR psz, _Outptr_ LPWSTR *ppwsz);
//      The caller passes the address of an LPWSTR variable as ppwsz, and SHStrDupW allocates
//      and initializes memory and returns the pointer to the new LPWSTR in *ppwsz.
//
//    _Outptr_opt_ - describes parameters that are allowed to be NULL.
//    _Outptr_*_result_maybenull_ - describes parameters where the called function might return NULL to the caller.
//
//    Example:
//       void MyFunc(_Outptr_opt_ int **ppData1, _Outptr_result_maybenull_ int **ppData2);
//    Callers:
//       MyFunc(NULL, NULL);           // error: parameter 2, ppData2, should not be NULL
//       MyFunc(&pData1, &pData2);     // ok: both non-NULL
//       if (*pData1 == *pData2) ...   // error: pData2 might be NULL after call






// Annotations for _Outptr_ parameters returning pointers to null terminated strings.






// Annotations for _Outptr_ parameters where the output pointer is set to NULL if the function fails.




// Annotations for _Outptr_ parameters which return a pointer to a ref-counted COM object,
// following the COM convention of setting the output to NULL on failure.
// The current implementation is identical to _Outptr_result_nullonfailure_.
// For pointers to types that are not COM objects, _Outptr_result_nullonfailure_ is preferred.






// Annotations for _Outptr_ parameters returning a pointer to buffer with a specified number of elements/bytes

































// Annotations for output reference to pointer parameters.


















// Annotations for output reference to pointer parameters that guarantee
// that the pointer is set to NULL on failure.


// Generic annotations to set output value of a by-pointer or by-reference parameter to null/zero on failure.




// return values -------------------------------

//
// _Ret_ annotations
//
// describing conditions that hold for return values after the call

// e.g. _Ret_z_ CString::operator const wchar_t*() const noexcept;



// used with allocated but not yet initialized objects




// used with allocated and initialized objects
//    returns single valid object


//    returns pointer to initialized buffer of specified size







//    returns pointer to partially initialized buffer, with total size 'size' and initialized size 'count'






// Annotations for strict type checking




// Check the return value of a function e.g. _Check_return_ ErrorCode Foo();



// e.g. MyPrintF( _Printf_format_string_ const wchar_t* wzFormat, ... );









// annotations to express value of integral or pointer parameter









// annotation to express that a value (usually a field of a mutable class)
// is not changed by a function call


// Annotations to allow expressing generalized pre and post conditions.
// 'cond' may be any valid SAL expression that is considered to be true as a precondition
// or postcondition (respsectively).



// Annotations to express struct, class and field invariants




















//============================================================================
//   _Pre_/_Post_ Layer:
//============================================================================

//
// Raw Pre/Post for declaring custom pre/post conditions
//




//
// Validity property
//





//
// Buffer size properties
//

// Expressing buffer sizes without specifying pre or post condition








// Expressing buffer size as pre or post condition










//
// Pointer null-ness properties
//




//
// _Pre_ annotations ---
//
// describing conditions that must be met before the call of the function

// e.g. int strlen( _Pre_z_ const char* sz );
// buffer is a zero terminated string


// valid size unknown or indicated by type (e.g.:LPSTR)





// Overrides recursive valid when some field is not yet initialized when using _Inout_


// used with allocated but not yet initialized objects




//
// _Post_ annotations ---
//
// describing conditions that hold after the function call

// void CopyStr( _In_z_ const char* szFrom, _Pre_cap_(cch) _Post_z_ char* szFrom, size_t cchFrom );
// buffer will be a zero-terminated string after the call


// e.g. HRESULT InitStruct( _Post_valid_ Struct* pobj );



// e.g. void free( _Post_ptr_invalid_ void* pv );


// e.g. void ThrowExceptionIfNull( _Post_notnull_ const void* pv );


// e.g. HRESULT GetObject(_Outptr_ _On_failure_(_At_(*p, _Post_null_)) T **p);







#pragma region Input Buffer SAL 1 compatibility macros

/*==========================================================================

   This section contains definitions for macros defined for VS2010 and earlier.
   Usage of these macros is still supported, but the SAL 2 macros defined above
   are recommended instead.  This comment block is retained to assist in
   understanding SAL that still uses the older syntax.

   The macros are defined in 3 layers:

   _In_/_Out_ Layer:
   ----------------
   This layer provides the highest abstraction and its macros should be used
   in most cases. Its macros start with _In_, _Out_ or _Inout_. For the
   typical case they provide the most concise annotations.

   _Pre_/_Post_ Layer:
   ------------------
   The macros of this layer only should be used when there is no suitable macro
   in the _In_/_Out_ layer. Its macros start with _Pre_, _Post_, _Ret_,
   _Deref_pre_ _Deref_post_ and _Deref_ret_. This layer provides the most
   flexibility for annotations.

   Implementation Abstraction Layer:
   --------------------------------
   Macros from this layer should never be used directly. The layer only exists
   to hide the implementation of the annotation macros.


   Annotation Syntax:
   |--------------|----------|----------------|-----------------------------|
   |   Usage      | Nullness | ZeroTerminated |  Extent                     |
   |--------------|----------|----------------|-----------------------------|
   | _In_         | <>       | <>             | <>                          |
   | _Out_        | opt_     | z_             | [byte]cap_[c_|x_]( size )   |
   | _Inout_      |          |                | [byte]count_[c_|x_]( size ) |
   | _Deref_out_  |          |                | ptrdiff_cap_( ptr )         |
   |--------------|          |                | ptrdiff_count_( ptr )       |
   | _Ret_        |          |                |                             |
   | _Deref_ret_  |          |                |                             |
   |--------------|          |                |                             |
   | _Pre_        |          |                |                             |
   | _Post_       |          |                |                             |
   | _Deref_pre_  |          |                |                             |
   | _Deref_post_ |          |                |                             |
   |--------------|----------|----------------|-----------------------------|

   Usage:
   -----
   _In_, _Out_, _Inout_, _Pre_, _Post_, _Deref_pre_, _Deref_post_ are for
   formal parameters.
   _Ret_, _Deref_ret_ must be used for return values.

   Nullness:
   --------
   If the pointer can be NULL the annotation contains _opt. If the macro
   does not contain '_opt' the pointer may not be NULL.

   String Type:
   -----------
   _z: NullTerminated string
   for _In_ parameters the buffer must have the specified stringtype before the call
   for _Out_ parameters the buffer must have the specified stringtype after the call
   for _Inout_ parameters both conditions apply

   Extent Syntax:
   |------|---------------|---------------|
   | Unit | Writ/Readable | Argument Type |
   |------|---------------|---------------|
   |  <>  | cap_          | <>            |
   | byte | count_        | c_            |
   |      |               | x_            |
   |------|---------------|---------------|

   'cap' (capacity) describes the writable size of the buffer and is typically used
   with _Out_. The default unit is elements. Use 'bytecap' if the size is given in bytes
   'count' describes the readable size of the buffer and is typically used with _In_.
   The default unit is elements. Use 'bytecount' if the size is given in bytes.

   Argument syntax for cap_, bytecap_, count_, bytecount_:
   (<parameter>|return)[+n]  e.g. cch, return, cb+2

   If the buffer size is a constant expression use the c_ postfix.
   E.g. cap_c_(20), count_c_(MAX_PATH), bytecount_c_(16)

   If the buffer size is given by a limiting pointer use the ptrdiff_ versions
   of the macros.

   If the buffer size is neither a parameter nor a constant expression use the x_
   postfix. e.g. bytecount_x_(num*size) x_ annotations accept any arbitrary string.
   No analysis can be done for x_ annotations but they at least tell the tool that
   the buffer has some sort of extent description. x_ annotations might be supported
   by future compiler versions.

============================================================================*/

// e.g. void SetCharRange( _In_count_(cch) const char* rgch, size_t cch )
// valid buffer extent described by another parameter





// valid buffer extent described by a constant extression





// nullterminated  'input' buffers with given size

// e.g. void SetCharRange( _In_count_(cch) const char* rgch, size_t cch )
// nullterminated valid buffer extent described by another parameter





// nullterminated valid buffer extent described by a constant extression





// buffer capacity is described by another pointer
// e.g. void Foo( _In_ptrdiff_count_(pchMax) const char* pch, const char* pchMax ) { while pch < pchMax ) pch++; }



// 'x' version for complex expressions that are not supported by the current compiler version
// e.g. void Set3ColMatrix( _In_count_x_(3*cRows) const Elem* matrix, int cRows );






// 'out' with buffer size
// e.g. void GetIndeces( _Out_cap_(cIndeces) int* rgIndeces, size_t cIndices );
// buffer capacity is described by another parameter





// buffer capacity is described by a constant expression





// buffer capacity is described by another parameter multiplied by a constant expression





// buffer capacity is described by another pointer
// e.g. void Foo( _Out_ptrdiff_cap_(pchMax) char* pch, const char* pchMax ) { while pch < pchMax ) pch++; }



// buffer capacity is described by a complex expression





// a zero terminated string is filled into a buffer of given capacity
// e.g. void CopyStr( _In_z_ const char* szFrom, _Out_z_cap_(cchTo) char* szTo, size_t cchTo );
// buffer capacity is described by another parameter





// buffer capacity is described by a constant expression





// buffer capacity is described by a complex expression





// a zero terminated string is filled into a buffer of given capacity
// e.g. size_t CopyCharRange( _In_count_(cchFrom) const char* rgchFrom, size_t cchFrom, _Out_cap_post_count_(cchTo,return)) char* rgchTo, size_t cchTo );





// a zero terminated string is filled into a buffer of given capacity
// e.g. size_t CopyStr( _In_z_ const char* szFrom, _Out_z_cap_post_count_(cchTo,return+1) char* szTo, size_t cchTo );





// only use with dereferenced arguments e.g. '*pcch'










// e.g. GetString( _Out_z_capcount_(*pLen+1) char* sz, size_t* pLen );






// 'inout' buffers with initialized elements before and after the call
// e.g. void ModifyIndices( _Inout_count_(cIndices) int* rgIndeces, size_t cIndices );










// nullterminated 'inout' buffers with initialized elements before and after the call
// e.g. void ModifyIndices( _Inout_count_(cIndices) int* rgIndeces, size_t cIndices );


















// e.g. void AppendToLPSTR( _In_ LPCSTR szFrom, _Inout_cap_(cchTo) LPSTR* szTo, size_t cchTo );















// inout string buffers with writable size
// e.g. void AppendStr( _In_z_ const char* szFrom, _Inout_z_cap_(cchTo) char* szTo, size_t cchTo );
















// returning pointers to valid objects



// annotations to express 'boundedness' of integral value parameter








// e.g.  HRESULT HrCreatePoint( _Deref_out_opt_ POINT** ppPT );





// e.g.  void CloneString( _In_z_ const wchar_t* wzFrom, _Deref_out_z_ wchar_t** pWzTo );





//
// _Deref_pre_ ---
//
// describing conditions for array elements of dereferenced pointer parameters that must be met before the call

// e.g. void SaveStringArray( _In_count_(cStrings) _Deref_pre_z_ const wchar_t* const rgpwch[] );



// e.g. void FillInArrayOfStr32( _In_count_(cStrings) _Deref_pre_cap_c_(32) _Deref_post_z_ wchar_t* const rgpwch[] );
// buffer capacity is described by another parameter





// buffer capacity is described by a constant expression





// buffer capacity is described by a complex condition





// convenience macros for nullterminated buffers with given capacity















// known capacity and valid but unknown readable extent















// e.g. void SaveMatrix( _In_count_(n) _Deref_pre_count_(n) const Elem** matrix, size_t n );
// valid buffer extent is described by another parameter





// valid buffer extent is described by a constant expression





// valid buffer extent is described by a complex expression





// e.g. void PrintStringArray( _In_count_(cElems) _Deref_pre_valid_ LPCSTR rgStr[], size_t cElems );








// restrict access rights



//
// _Deref_post_ ---
//
// describing conditions for array elements or dereferenced pointer parameters that hold after the call

// e.g. void CloneString( _In_z_ const Wchar_t* wzIn _Out_ _Deref_post_z_ wchar_t** pWzOut );



// e.g. HRESULT HrAllocateMemory( size_t cb, _Out_ _Deref_post_bytecap_(cb) void** ppv );
// buffer capacity is described by another parameter





// buffer capacity is described by a constant expression





// buffer capacity is described by a complex expression





// convenience macros for nullterminated buffers with given capacity















// known capacity and valid but unknown readable extent















// e.g. HRESULT HrAllocateZeroInitializedMemory( size_t cb, _Out_ _Deref_post_bytecount_(cb) void** ppv );
// valid buffer extent is described by another parameter





// buffer capacity is described by a constant expression





// buffer capacity is described by a complex expression





// e.g. void GetStrings( _Out_count_(cElems) _Deref_post_valid_ LPSTR const rgStr[], size_t cElems );







//
// _Deref_ret_ ---
//




//
// special _Deref_ ---
//


//
// _Ret_ ---
//

// e.g. _Ret_opt_valid_ LPSTR void* CloneSTR( _Pre_valid_ LPSTR src );



// e.g. _Ret_opt_bytecap_(cb) void* AllocateMemory( size_t cb );
// Buffer capacity is described by another parameter





// Buffer capacity is described by a constant expression





// Buffer capacity is described by a complex condition





// return value is nullterminated and capacity is given by another parameter





// e.g. _Ret_opt_bytecount_(cb) void* AllocateZeroInitializedMemory( size_t cb );
// Valid Buffer extent is described by another parameter





// Valid Buffer extent is described by a constant expression





// Valid Buffer extent is described by a complex expression





// return value is nullterminated and length is given by another parameter






// _Pre_ annotations ---


// restrict access rights



// e.g. void FreeMemory( _Pre_bytecap_(cb) _Post_ptr_invalid_ void* pv, size_t cb );
// buffer capacity described by another parameter





// buffer capacity described by a constant expression







// buffer capacity is described by another parameter multiplied by a constant expression



// buffer capacity described by size of other buffer, only used by dangerous legacy APIs
// e.g. int strcpy(_Pre_cap_for_(src) char* dst, const char* src);



// buffer capacity described by a complex condition





// buffer capacity described by the difference to another pointer parameter



// e.g. void AppendStr( _Pre_z_ const char* szFrom, _Pre_z_cap_(cchTo) _Post_z_ char* szTo, size_t cchTo );















// known capacity and valid but unknown readable extent















// e.g. void AppendCharRange( _Pre_count_(cchFrom) const char* rgFrom, size_t cchFrom, _Out_z_cap_(cchTo) char* szTo, size_t cchTo );
// Valid buffer extent described by another parameter





// Valid buffer extent described by a constant expression





// Valid buffer extent described by a complex expression





// Valid buffer extent described by the difference to another pointer parameter




// char * strncpy(_Out_cap_(_Count) _Post_maybez_ char * _Dest, _In_z_ const char * _Source, _In_ size_t _Count)
// buffer maybe zero-terminated after the call


// e.g. SIZE_T HeapSize( _In_ HANDLE hHeap, DWORD dwFlags, _Pre_notnull_ _Post_bytecap_(return) LPCVOID lpMem );



// e.g. int strlen( _In_z_ _Post_count_(return+1) const char* sz );







// e.g. size_t CopyStr( _In_z_ const char* szFrom, _Pre_cap_(cch) _Post_z_count_(return+1) char* szFrom, size_t cchFrom );







//
// _Prepost_ ---
//
// describing conditions that hold before and after the function call



















//
// _Deref_<both> ---
//
// short version for _Deref_pre_<ann> _Deref_post_<ann>
// describing conditions for array elements or dereferenced pointer parameters that hold before and after the call










































//
// _Deref_<miscellaneous>
//
// used with references to arrays







#pragma endregion Input Buffer SAL 1 compatibility macros


//============================================================================
//   Implementation Layer:
//============================================================================


// Naming conventions:
// A symbol the begins with _SA_ is for the machinery of creating any
// annotations; many of those come from sourceannotations.h in the case
// of attributes.

// A symbol that ends with _impl is the very lowest level macro.  It is
// not required to be a legal standalone annotation, and in the case
// of attribute annotations, usually is not.  (In the case of some declspec
// annotations, it might be, but it should not be assumed so.)  Those
// symols will be used in the _PreN..., _PostN... and _RetN... annotations
// to build up more complete annotations.

// A symbol ending in _impl_ is reserved to the implementation as well,
// but it does form a complete annotation; usually they are used to build
// up even higher level annotations.





















































































































// Using "nothing" for sal



















































































































































































































































































































































































































































































































































// Obsolete -- may be needed for transition to attributes.





// This section contains the deprecated annotations

/*
 -------------------------------------------------------------------------------
 Introduction

 sal.h provides a set of annotations to describe how a function uses its
 parameters - the assumptions it makes about them, and the guarantees it makes
 upon finishing.

 Annotations may be placed before either a function parameter's type or its return
 type, and describe the function's behavior regarding the parameter or return value.
 There are two classes of annotations: buffer annotations and advanced annotations.
 Buffer annotations describe how functions use their pointer parameters, and
 advanced annotations either describe complex/unusual buffer behavior, or provide
 additional information about a parameter that is not otherwise expressible.

 -------------------------------------------------------------------------------
 Buffer Annotations

 The most important annotations in sal.h provide a consistent way to annotate
 buffer parameters or return values for a function. Each of these annotations describes
 a single buffer (which could be a string, a fixed-length or variable-length array,
 or just a pointer) that the function interacts with: where it is, how large it is,
 how much is initialized, and what the function does with it.

 The appropriate macro for a given buffer can be constructed using the table below.
 Just pick the appropriate values from each category, and combine them together
 with a leading underscore. Some combinations of values do not make sense as buffer
 annotations. Only meaningful annotations can be added to your code; for a list of
 these, see the buffer annotation definitions section.

 Only a single buffer annotation should be used for each parameter.

 |------------|------------|---------|--------|----------|----------|---------------|
 |   Level    |   Usage    |  Size   | Output | NullTerm | Optional |  Parameters   |
 |------------|------------|---------|--------|----------|----------|---------------|
 | <>         | <>         | <>      | <>     | _z       | <>       | <>            |
 | _deref     | _in        | _ecount | _full  | _nz      | _opt     | (size)        |
 | _deref_opt | _out       | _bcount | _part  |          |          | (size,length) |
 |            | _inout     |         |        |          |          |               |
 |            |            |         |        |          |          |               |
 |------------|------------|---------|--------|----------|----------|---------------|

 Level: Describes the buffer pointer's level of indirection from the parameter or
          return value 'p'.

 <>         : p is the buffer pointer.
 _deref     : *p is the buffer pointer. p must not be NULL.
 _deref_opt : *p may be the buffer pointer. p may be NULL, in which case the rest of
                the annotation is ignored.

 Usage: Describes how the function uses the buffer.

 <>     : The buffer is not accessed. If used on the return value or with _deref, the
            function will provide the buffer, and it will be uninitialized at exit.
            Otherwise, the caller must provide the buffer. This should only be used
            for alloc and free functions.
 _in    : The function will only read from the buffer. The caller must provide the
            buffer and initialize it. Cannot be used with _deref.
 _out   : The function will only write to the buffer. If used on the return value or
            with _deref, the function will provide the buffer and initialize it.
            Otherwise, the caller must provide the buffer, and the function will
            initialize it.
 _inout : The function may freely read from and write to the buffer. The caller must
            provide the buffer and initialize it. If used with _deref, the buffer may
            be reallocated by the function.

 Size: Describes the total size of the buffer. This may be less than the space actually
         allocated for the buffer, in which case it describes the accessible amount.

 <>      : No buffer size is given. If the type specifies the buffer size (such as
             with LPSTR and LPWSTR), that amount is used. Otherwise, the buffer is one
             element long. Must be used with _in, _out, or _inout.
 _ecount : The buffer size is an explicit element count.
 _bcount : The buffer size is an explicit byte count.

 Output: Describes how much of the buffer will be initialized by the function. For
           _inout buffers, this also describes how much is initialized at entry. Omit this
           category for _in buffers; they must be fully initialized by the caller.

 <>    : The type specifies how much is initialized. For instance, a function initializing
           an LPWSTR must NULL-terminate the string.
 _full : The function initializes the entire buffer.
 _part : The function initializes part of the buffer, and explicitly indicates how much.

 NullTerm: States if the present of a '\0' marks the end of valid elements in the buffer.
 _z    : A '\0' indicated the end of the buffer
 _nz     : The buffer may not be null terminated and a '\0' does not indicate the end of the
          buffer.
 Optional: Describes if the buffer itself is optional.

 <>   : The pointer to the buffer must not be NULL.
 _opt : The pointer to the buffer might be NULL. It will be checked before being dereferenced.

 Parameters: Gives explicit counts for the size and length of the buffer.

 <>            : There is no explicit count. Use when neither _ecount nor _bcount is used.
 (size)        : Only the buffer's total size is given. Use with _ecount or _bcount but not _part.
 (size,length) : The buffer's total size and initialized length are given. Use with _ecount_part
                   and _bcount_part.

 -------------------------------------------------------------------------------
 Buffer Annotation Examples

 LWSTDAPI_(BOOL) StrToIntExA(
     __in LPCSTR pszString,
     DWORD dwFlags,
     __out int *piRet                     -- A pointer whose dereference will be filled in.
 );

 void MyPaintingFunction(
     __in HWND hwndControl,               -- An initialized read-only parameter.
     __in_opt HDC hdcOptional,            -- An initialized read-only parameter that might be NULL.
     __inout IPropertyStore *ppsStore     -- An initialized parameter that may be freely used
                                          --   and modified.
 );

 LWSTDAPI_(BOOL) PathCompactPathExA(
     __out_ecount(cchMax) LPSTR pszOut,   -- A string buffer with cch elements that will
                                          --   be NULL terminated on exit.
     __in LPCSTR pszSrc,
     UINT cchMax,
     DWORD dwFlags
 );

 HRESULT SHLocalAllocBytes(
     size_t cb,
     __deref_bcount(cb) T **ppv           -- A pointer whose dereference will be set to an
                                          --   uninitialized buffer with cb bytes.
 );

 __inout_bcount_full(cb) : A buffer with cb elements that is fully initialized at
     entry and exit, and may be written to by this function.

 __out_ecount_part(count, *countOut) : A buffer with count elements that will be
     partially initialized by this function. The function indicates how much it
     initialized by setting *countOut.

 -------------------------------------------------------------------------------
 Advanced Annotations

 Advanced annotations describe behavior that is not expressible with the regular
 buffer macros. These may be used either to annotate buffer parameters that involve
 complex or conditional behavior, or to enrich existing annotations with additional
 information.

 __success(expr) f :
     <expr> indicates whether function f succeeded or not. If <expr> is true at exit,
     all the function's guarantees (as given by other annotations) must hold. If <expr>
     is false at exit, the caller should not expect any of the function's guarantees
     to hold. If not used, the function must always satisfy its guarantees. Added
     automatically to functions that indicate success in standard ways, such as by
     returning an HRESULT.

 __nullterminated p :
     Pointer p is a buffer that may be read or written up to and including the first
     NULL character or pointer. May be used on typedefs, which marks valid (properly
     initialized) instances of that type as being NULL-terminated.

 __nullnullterminated p :
     Pointer p is a buffer that may be read or written up to and including the first
     sequence of two NULL characters or pointers. May be used on typedefs, which marks
     valid instances of that type as being double-NULL terminated.

 __reserved v :
     Value v must be 0/NULL, reserved for future use.

 __checkReturn v :
     Return value v must not be ignored by callers of this function.

 __typefix(ctype) v :
     Value v should be treated as an instance of ctype, rather than its declared type.

 __override f :
     Specify C#-style 'override' behaviour for overriding virtual methods.

 __callback f :
     Function f can be used as a function pointer.

 __format_string p :
     Pointer p is a string that contains % markers in the style of printf.

 __blocksOn(resource) f :
     Function f blocks on the resource 'resource'.

 __fallthrough :
     Annotates switch statement labels where fall-through is desired, to distinguish
     from forgotten break statements.

 -------------------------------------------------------------------------------
 Advanced Annotation Examples

 __success(return != FALSE) LWSTDAPI_(BOOL)
 PathCanonicalizeA(__out_ecount(MAX_PATH) LPSTR pszBuf, LPCSTR pszPath) :
    pszBuf is only guaranteed to be NULL-terminated when TRUE is returned.

 typedef __nullterminated WCHAR* LPWSTR : Initialized LPWSTRs are NULL-terminated strings.

 __out_ecount(cch) __typefix(LPWSTR) void *psz : psz is a buffer parameter which will be
     a NULL-terminated WCHAR string at exit, and which initially contains cch WCHARs.

 -------------------------------------------------------------------------------
*/















/*
 -------------------------------------------------------------------------------
 Helper Macro Definitions

 These express behavior common to many of the high-level annotations.
 DO NOT USE THESE IN YOUR CODE.
 -------------------------------------------------------------------------------
*/

/*
    The helper annotations are only understood by the compiler version used by
    various defect detection tools. When the regular compiler is running, they
    are defined into nothing, and do not affect the compiled code.
*/




















































































































































































































    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    

    
    
    
    

    
    



/*
-------------------------------------------------------------------------------
Buffer Annotation Definitions

Any of these may be used to directly annotate functions, but only one should
be used for each parameter. To determine which annotation to use for a given
buffer, use the table in the buffer annotations section.
-------------------------------------------------------------------------------
*/
































































































































































































/*
-------------------------------------------------------------------------------
Advanced Annotation Definitions

Any of these may be used to directly annotate functions, and may be used in
combination with each other or with regular buffer macros. For an explanation
of each annotation, see the advanced annotations section.
-------------------------------------------------------------------------------
*/



































    
    






























//
// Set the analysis mode (global flags to analysis).
// They take effect at the point of declaration; use at global scope
// as a declaration.
//

// Synthesize a unique symbol.








//
// Floating point warnings are only meaningful in kernel-mode on x86
// so avoid reporting them on other platforms.
//















// The following are predefined:
//  _Analysis_operator_new_throw_   (operator new throws)
//  _Analysis_operator_new_null_        (operator new returns null)
//  _Analysis_operator_new_never_fails_ (operator new never fails)
//

// Function class annotations.


















/***
*concurrencysal.h - markers for documenting the concurrent semantics of APIs
*
*       Copyright (c) Microsoft Corporation. All rights reserved.
*
*Purpose:
*       This file contains macros for Concurrency SAL annotations. Definitions
*       starting with _Internal are low level macros that are subject to change.
*       Users should not use those low level macros directly.
*       [ANSI]
*
*       [Public]
*
****/




#pragma once






































































































































































































































































































/*
 * Old spelling: will be deprecated
 */













































//
// vadefs.h
//
//      Copyright (c) Microsoft Corporation. All rights reserved.
//
// Definitions of macro helpers used by <stdarg.h>.  This is the topmost header
// in the CRT header lattice, and is always the first CRT header to be included,
// explicitly or implicitly.  Therefore, this header also has several definitions
// that are used throughout the CRT.
//
#pragma once



#pragma pack(push, 8)










    
    
        typedef unsigned __int64  uintptr_t;
    





    
    


        typedef char* va_list;
    





    











    
    




















































    void __cdecl __va_start(va_list* , ...);

    
    



    








































    



#pragma pack(pop)


// All C headers have a common prologue and epilogue, to enclose the header in
// an extern "C" declaration when the header is #included in a C++ translation
// unit and to push/pop the packing.






















    


    




__pragma(pack(push, 8))




    


        
    

















    




        
    



    


        
    


// Definitions of calling conventions used code sometimes compiled as managed




    
    





    




// Definitions of common __declspecs




    







    





    



    



      
    


// For backwards compatibility


// Definitions of common types

    typedef unsigned __int64 size_t;
    typedef __int64          ptrdiff_t;
    typedef __int64          intptr_t;













    typedef _Bool __vcrt_bool;


// Indicate that these common types are defined

    



    



    


// Provide a typedef for wchar_t for use under /Zc:wchar_t-

    
    typedef unsigned short wchar_t;



    


        
    



    













    


// See note on use of "deprecate" at the top of this file







    


        




    







    
        
    






    void __cdecl __security_init_cookie(void);

    



        void __cdecl __security_check_cookie(  uintptr_t _StackCookie);
        __declspec(noreturn) void __cdecl __report_gsfailure(  uintptr_t _StackCookie);
    


extern uintptr_t __security_cookie;


    
    
    


__pragma(pack(pop))




__pragma(pack(push, 8))










__pragma(pack(pop))







    


//----------------------------------------------------------------------------------
// Some basic Defines
//----------------------------------------------------------------------------------

    







// Allow custom memory allocators

    


    


    


// NOTE: MSC C++ compiler does not support compound literals (C99 feature)
// Plain structures in C++ (without constructors) can be initialized from { } initializers.



    


// Some Basic Colors
// NOTE: Custom raylib color palette for amazing visuals on WHITE background




























// Temporal hack to avoid breaking old codebases using
// deprecated raylib implementation of these functions




//----------------------------------------------------------------------------------
// Structures Definition
//----------------------------------------------------------------------------------
// Boolean type



    typedef enum { false, true } bool;


// Vector2 type
typedef struct Vector2 {
    float x;
    float y;
} Vector2;

// Vector3 type
typedef struct Vector3 {
    float x;
    float y;
    float z;
} Vector3;

// Vector4 type
typedef struct Vector4 {
    float x;
    float y;
    float z;
    float w;
} Vector4;

// Quaternion type, same as Vector4
typedef Vector4 Quaternion;

// Matrix type (OpenGL style 4x4 - right handed, column major)
typedef struct Matrix {
    float m0, m4, m8, m12;
    float m1, m5, m9, m13;
    float m2, m6, m10, m14;
    float m3, m7, m11, m15;
} Matrix;

// Color type, RGBA (32bit)
typedef struct Color {
    unsigned char r;
    unsigned char g;
    unsigned char b;
    unsigned char a;
} Color;

// Rectangle type
typedef struct Rectangle {
    float x;
    float y;
    float width;
    float height;
} Rectangle;

// Image type, bpp always RGBA (32bit)
// NOTE: Data stored in CPU memory (RAM)
typedef struct Image {
    void *data;             // Image raw data
    int width;              // Image base width
    int height;             // Image base height
    int mipmaps;            // Mipmap levels, 1 by default
    int format;             // Data format (PixelFormat type)
} Image;

// Texture2D type
// NOTE: Data stored in GPU memory
typedef struct Texture2D {
    unsigned int id;        // OpenGL texture id
    int width;              // Texture base width
    int height;             // Texture base height
    int mipmaps;            // Mipmap levels, 1 by default
    int format;             // Data format (PixelFormat type)
} Texture2D;

// Texture type, same as Texture2D
typedef Texture2D Texture;

// TextureCubemap type, actually, same as Texture2D
typedef Texture2D TextureCubemap;

// RenderTexture2D type, for texture rendering
typedef struct RenderTexture2D {
    unsigned int id;        // OpenGL Framebuffer Object (FBO) id
    Texture2D texture;      // Color buffer attachment texture
    Texture2D depth;        // Depth buffer attachment texture
    bool depthTexture;      // Track if depth attachment is a texture or renderbuffer
} RenderTexture2D;

// RenderTexture type, same as RenderTexture2D
typedef RenderTexture2D RenderTexture;

// N-Patch layout info
typedef struct NPatchInfo {
    Rectangle sourceRec;   // Region in the texture
    int left;              // left border offset
    int top;               // top border offset
    int right;             // right border offset
    int bottom;            // bottom border offset
    int type;              // layout of the n-patch: 3x3, 1x3 or 3x1
} NPatchInfo;

// Font character info
typedef struct CharInfo {
    int value;              // Character value (Unicode)
    int offsetX;            // Character offset X when drawing
    int offsetY;            // Character offset Y when drawing
    int advanceX;           // Character advance position X
    Image image;            // Character image data
} CharInfo;

// Font type, includes texture and charSet array data
typedef struct Font {
    int baseSize;           // Base size (default chars height)
    int charsCount;         // Number of characters
    Texture2D texture;      // Characters texture atlas
    Rectangle *recs;        // Characters rectangles in texture
    CharInfo *chars;        // Characters info data
} Font;



// Camera type, defines a camera position/orientation in 3d space
typedef struct Camera3D {
    Vector3 position;       // Camera position
    Vector3 target;         // Camera target it looks-at
    Vector3 up;             // Camera up vector (rotation over its axis)
    float fovy;             // Camera field-of-view apperture in Y (degrees) in perspective, used as near plane width in orthographic
    int type;               // Camera type, defines projection type: CAMERA_PERSPECTIVE or CAMERA_ORTHOGRAPHIC
} Camera3D;

typedef Camera3D Camera;    // Camera type fallback, defaults to Camera3D

// Camera2D type, defines a 2d camera
typedef struct Camera2D {
    Vector2 offset;         // Camera offset (displacement from target)
    Vector2 target;         // Camera target (rotation and zoom origin)
    float rotation;         // Camera rotation in degrees
    float zoom;             // Camera zoom (scaling), should be 1.0f by default
} Camera2D;

// Vertex data definning a mesh
// NOTE: Data stored in CPU memory (and GPU)
typedef struct Mesh {
    int vertexCount;        // Number of vertices stored in arrays
    int triangleCount;      // Number of triangles stored (indexed or not)

    // Default vertex data
    float *vertices;        // Vertex position (XYZ - 3 components per vertex) (shader-location = 0)
    float *texcoords;       // Vertex texture coordinates (UV - 2 components per vertex) (shader-location = 1)
    float *texcoords2;      // Vertex second texture coordinates (useful for lightmaps) (shader-location = 5)
    float *normals;         // Vertex normals (XYZ - 3 components per vertex) (shader-location = 2)
    float *tangents;        // Vertex tangents (XYZW - 4 components per vertex) (shader-location = 4)
    unsigned char *colors;  // Vertex colors (RGBA - 4 components per vertex) (shader-location = 3)
    unsigned short *indices;// Vertex indices (in case vertex data comes indexed)

    // Animation vertex data
    float *animVertices;    // Animated vertex positions (after bones transformations)
    float *animNormals;     // Animated normals (after bones transformations)
    int *boneIds;           // Vertex bone ids, up to 4 bones influence by vertex (skinning)
    float *boneWeights;     // Vertex bone weight, up to 4 bones influence by vertex (skinning)

    // OpenGL identifiers
    unsigned int vaoId;     // OpenGL Vertex Array Object id
    unsigned int *vboId;    // OpenGL Vertex Buffer Objects id (default vertex data)
} Mesh;

// Shader type (generic)
typedef struct Shader {
    unsigned int id;        // Shader program id
    int *locs;              // Shader locations array (MAX_SHADER_LOCATIONS)
} Shader;

// Material texture map
typedef struct MaterialMap {
    Texture2D texture;      // Material map texture
    Color color;            // Material map color
    float value;            // Material map value
} MaterialMap;

// Material type (generic)
typedef struct Material {
    Shader shader;          // Material shader
    MaterialMap *maps;      // Material maps array (MAX_MATERIAL_MAPS)
    float *params;          // Material generic parameters (if required)
} Material;

// Transformation properties
typedef struct Transform {
    Vector3 translation;    // Translation
    Quaternion rotation;    // Rotation
    Vector3 scale;          // Scale
} Transform;

// Bone information
typedef struct BoneInfo {
    char name[32];          // Bone name
    int parent;             // Bone parent
} BoneInfo;

// Model type
typedef struct Model {
    Matrix transform;       // Local transform matrix

    int meshCount;          // Number of meshes
    Mesh *meshes;           // Meshes array

    int materialCount;      // Number of materials
    Material *materials;    // Materials array
    int *meshMaterial;      // Mesh material number

    // Animation data
    int boneCount;          // Number of bones
    BoneInfo *bones;        // Bones information (skeleton)
    Transform *bindPose;    // Bones base transformation (pose)
} Model;

// Model animation
typedef struct ModelAnimation {
    int boneCount;          // Number of bones
    BoneInfo *bones;        // Bones information (skeleton)

    int frameCount;         // Number of animation frames
    Transform **framePoses; // Poses array by frame
} ModelAnimation;

// Ray type (useful for raycast)
typedef struct Ray {
    Vector3 position;       // Ray position (origin)
    Vector3 direction;      // Ray direction
} Ray;

// Raycast hit information
typedef struct RayHitInfo {
    bool hit;               // Did the ray hit something?
    float distance;         // Distance to nearest hit
    Vector3 position;       // Position of nearest hit
    Vector3 normal;         // Surface normal of hit
} RayHitInfo;

// Bounding box type
typedef struct BoundingBox {
    Vector3 min;            // Minimum vertex box-corner
    Vector3 max;            // Maximum vertex box-corner
} BoundingBox;

// Wave type, defines audio wave data
typedef struct Wave {
    unsigned int sampleCount;       // Total number of samples
    unsigned int sampleRate;        // Frequency (samples per second)
    unsigned int sampleSize;        // Bit depth (bits per sample): 8, 16, 32 (24 not supported)
    unsigned int channels;          // Number of channels (1-mono, 2-stereo)
    void *data;                     // Buffer data pointer
} Wave;

typedef struct rAudioBuffer rAudioBuffer;

// Audio stream type
// NOTE: Useful to create custom audio streams not bound to a specific file
typedef struct AudioStream {
    unsigned int sampleRate;        // Frequency (samples per second)
    unsigned int sampleSize;        // Bit depth (bits per sample): 8, 16, 32 (24 not supported)
    unsigned int channels;          // Number of channels (1-mono, 2-stereo)

    rAudioBuffer *buffer;           // Pointer to internal data used by the audio system
} AudioStream;

// Sound source type
typedef struct Sound {
    unsigned int sampleCount;       // Total number of samples
    AudioStream stream;             // Audio stream
} Sound;

// Music stream type (audio file streaming from memory)
// NOTE: Anything longer than ~10 seconds should be streamed
typedef struct Music {
    int ctxType;                    // Type of music context (audio filetype)
    void *ctxData;                  // Audio context data, depends on type
    
    unsigned int sampleCount;       // Total number of samples
    unsigned int sampleLeft;        // Number of samples left to end
    unsigned int loopCount;         // Loops count (times music will play), 0 means infinite loop

    AudioStream stream;             // Audio stream
} Music;

// Head-Mounted-Display device parameters
typedef struct VrDeviceInfo {
    int hResolution;                // HMD horizontal resolution in pixels
    int vResolution;                // HMD vertical resolution in pixels
    float hScreenSize;              // HMD horizontal size in meters
    float vScreenSize;              // HMD vertical size in meters
    float vScreenCenter;            // HMD screen center in meters
    float eyeToScreenDistance;      // HMD distance between eye and display in meters
    float lensSeparationDistance;   // HMD lens separation distance in meters
    float interpupillaryDistance;   // HMD IPD (distance between pupils) in meters
    float lensDistortionValues[4];  // HMD lens distortion constant parameters
    float chromaAbCorrection[4];    // HMD chromatic aberration correction parameters
} VrDeviceInfo;

//----------------------------------------------------------------------------------
// Enumerators Definition
//----------------------------------------------------------------------------------
// System config flags
// NOTE: Used for bit masks
typedef enum {
    FLAG_SHOW_LOGO          = 1,    // Set to show raylib logo at startup
    FLAG_FULLSCREEN_MODE    = 2,    // Set to run program in fullscreen
    FLAG_WINDOW_RESIZABLE   = 4,    // Set to allow resizable window
    FLAG_WINDOW_UNDECORATED = 8,    // Set to disable window decoration (frame and buttons)
    FLAG_WINDOW_TRANSPARENT = 16,   // Set to allow transparent window
    FLAG_WINDOW_HIDDEN      = 128,  // Set to create the window initially hidden
    FLAG_WINDOW_ALWAYS_RUN  = 256,  // Set to allow windows running while minimized
    FLAG_MSAA_4X_HINT       = 32,   // Set to try enabling MSAA 4X
    FLAG_VSYNC_HINT         = 64    // Set to try enabling V-Sync on GPU
} ConfigFlag;

// Trace log type
typedef enum {
    LOG_ALL = 0,        // Display all logs
    LOG_TRACE,
    LOG_DEBUG,
    LOG_INFO,
    LOG_WARNING,
    LOG_ERROR,
    LOG_FATAL,
    LOG_NONE            // Disable logging
} TraceLogType;

// Keyboard keys
typedef enum {
    // Alphanumeric keys
    KEY_APOSTROPHE      = 39,
    KEY_COMMA           = 44,
    KEY_MINUS           = 45,
    KEY_PERIOD          = 46,
    KEY_SLASH           = 47,
    KEY_ZERO            = 48,
    KEY_ONE             = 49,
    KEY_TWO             = 50,
    KEY_THREE           = 51,
    KEY_FOUR            = 52,
    KEY_FIVE            = 53,
    KEY_SIX             = 54,
    KEY_SEVEN           = 55,
    KEY_EIGHT           = 56,
    KEY_NINE            = 57,
    KEY_SEMICOLON       = 59,
    KEY_EQUAL           = 61,
    KEY_A               = 65,
    KEY_B               = 66,
    KEY_C               = 67,
    KEY_D               = 68,
    KEY_E               = 69,
    KEY_F               = 70,
    KEY_G               = 71,
    KEY_H               = 72,
    KEY_I               = 73,
    KEY_J               = 74,
    KEY_K               = 75,
    KEY_L               = 76,
    KEY_M               = 77,
    KEY_N               = 78,
    KEY_O               = 79,
    KEY_P               = 80,
    KEY_Q               = 81,
    KEY_R               = 82,
    KEY_S               = 83,
    KEY_T               = 84,
    KEY_U               = 85,
    KEY_V               = 86,
    KEY_W               = 87,
    KEY_X               = 88,
    KEY_Y               = 89,
    KEY_Z               = 90,

    // Function keys
    KEY_SPACE           = 32,
    KEY_ESCAPE          = 256,
    KEY_ENTER           = 257,
    KEY_TAB             = 258,
    KEY_BACKSPACE       = 259,
    KEY_INSERT          = 260,
    KEY_DELETE          = 261,
    KEY_RIGHT           = 262,
    KEY_LEFT            = 263,
    KEY_DOWN            = 264,
    KEY_UP              = 265,
    KEY_PAGE_UP         = 266,
    KEY_PAGE_DOWN       = 267,
    KEY_HOME            = 268,
    KEY_END             = 269,
    KEY_CAPS_LOCK       = 280,
    KEY_SCROLL_LOCK     = 281,
    KEY_NUM_LOCK        = 282,
    KEY_PRINT_SCREEN    = 283,
    KEY_PAUSE           = 284,
    KEY_F1              = 290,
    KEY_F2              = 291,
    KEY_F3              = 292,
    KEY_F4              = 293,
    KEY_F5              = 294,
    KEY_F6              = 295,
    KEY_F7              = 296,
    KEY_F8              = 297,
    KEY_F9              = 298,
    KEY_F10             = 299,
    KEY_F11             = 300,
    KEY_F12             = 301,
    KEY_LEFT_SHIFT      = 340,
    KEY_LEFT_CONTROL    = 341,
    KEY_LEFT_ALT        = 342,
    KEY_LEFT_SUPER      = 343,
    KEY_RIGHT_SHIFT     = 344,
    KEY_RIGHT_CONTROL   = 345,
    KEY_RIGHT_ALT       = 346,
    KEY_RIGHT_SUPER     = 347,
    KEY_KB_MENU         = 348,
    KEY_LEFT_BRACKET    = 91,
    KEY_BACKSLASH       = 92,
    KEY_RIGHT_BRACKET   = 93,
    KEY_GRAVE           = 96,

    // Keypad keys
    KEY_KP_0            = 320,
    KEY_KP_1            = 321,
    KEY_KP_2            = 322,
    KEY_KP_3            = 323,
    KEY_KP_4            = 324,
    KEY_KP_5            = 325,
    KEY_KP_6            = 326,
    KEY_KP_7            = 327,
    KEY_KP_8            = 328,
    KEY_KP_9            = 329,
    KEY_KP_DECIMAL      = 330,
    KEY_KP_DIVIDE       = 331,
    KEY_KP_MULTIPLY     = 332,
    KEY_KP_SUBTRACT     = 333,
    KEY_KP_ADD          = 334,
    KEY_KP_ENTER        = 335,
    KEY_KP_EQUAL        = 336
} KeyboardKey;

// Android buttons
typedef enum {
    KEY_BACK            = 4,
    KEY_MENU            = 82,
    KEY_VOLUME_UP       = 24,
    KEY_VOLUME_DOWN     = 25
} AndroidButton;

// Mouse buttons
typedef enum {
    MOUSE_LEFT_BUTTON   = 0,
    MOUSE_RIGHT_BUTTON  = 1,
    MOUSE_MIDDLE_BUTTON = 2
} MouseButton;

// Gamepad number
typedef enum {
    GAMEPAD_PLAYER1     = 0,
    GAMEPAD_PLAYER2     = 1,
    GAMEPAD_PLAYER3     = 2,
    GAMEPAD_PLAYER4     = 3
} GamepadNumber;

// Gamepad Buttons
typedef enum {
    // This is here just for error checking
    GAMEPAD_BUTTON_UNKNOWN = 0,

    // This is normally [A,B,X,Y]/[Circle,Triangle,Square,Cross]
    // No support for 6 button controllers though..
    GAMEPAD_BUTTON_LEFT_FACE_UP,
    GAMEPAD_BUTTON_LEFT_FACE_RIGHT,
    GAMEPAD_BUTTON_LEFT_FACE_DOWN,
    GAMEPAD_BUTTON_LEFT_FACE_LEFT,

    // This is normally a DPAD
    GAMEPAD_BUTTON_RIGHT_FACE_UP,
    GAMEPAD_BUTTON_RIGHT_FACE_RIGHT,
    GAMEPAD_BUTTON_RIGHT_FACE_DOWN,
    GAMEPAD_BUTTON_RIGHT_FACE_LEFT,

    // Triggers
    GAMEPAD_BUTTON_LEFT_TRIGGER_1,
    GAMEPAD_BUTTON_LEFT_TRIGGER_2,
    GAMEPAD_BUTTON_RIGHT_TRIGGER_1,
    GAMEPAD_BUTTON_RIGHT_TRIGGER_2,

    // These are buttons in the center of the gamepad
    GAMEPAD_BUTTON_MIDDLE_LEFT,     //PS3 Select
    GAMEPAD_BUTTON_MIDDLE,          //PS Button/XBOX Button
    GAMEPAD_BUTTON_MIDDLE_RIGHT,    //PS3 Start

    // These are the joystick press in buttons
    GAMEPAD_BUTTON_LEFT_THUMB,
    GAMEPAD_BUTTON_RIGHT_THUMB
} GamepadButton;

typedef enum {
    // This is here just for error checking
    GAMEPAD_AXIS_UNKNOWN = 0,

    // Left stick
    GAMEPAD_AXIS_LEFT_X,
    GAMEPAD_AXIS_LEFT_Y,

    // Right stick
    GAMEPAD_AXIS_RIGHT_X,
    GAMEPAD_AXIS_RIGHT_Y,

    // Pressure levels for the back triggers
    GAMEPAD_AXIS_LEFT_TRIGGER,      // [1..-1] (pressure-level)
    GAMEPAD_AXIS_RIGHT_TRIGGER      // [1..-1] (pressure-level)
} GamepadAxis;

// Shader location point type
typedef enum {
    LOC_VERTEX_POSITION = 0,
    LOC_VERTEX_TEXCOORD01,
    LOC_VERTEX_TEXCOORD02,
    LOC_VERTEX_NORMAL,
    LOC_VERTEX_TANGENT,
    LOC_VERTEX_COLOR,
    LOC_MATRIX_MVP,
    LOC_MATRIX_MODEL,
    LOC_MATRIX_VIEW,
    LOC_MATRIX_PROJECTION,
    LOC_VECTOR_VIEW,
    LOC_COLOR_DIFFUSE,
    LOC_COLOR_SPECULAR,
    LOC_COLOR_AMBIENT,
    LOC_MAP_ALBEDO,          // LOC_MAP_DIFFUSE
    LOC_MAP_METALNESS,       // LOC_MAP_SPECULAR
    LOC_MAP_NORMAL,
    LOC_MAP_ROUGHNESS,
    LOC_MAP_OCCLUSION,
    LOC_MAP_EMISSION,
    LOC_MAP_HEIGHT,
    LOC_MAP_CUBEMAP,
    LOC_MAP_IRRADIANCE,
    LOC_MAP_PREFILTER,
    LOC_MAP_BRDF
} ShaderLocationIndex;




// Shader uniform data types
typedef enum {
    UNIFORM_FLOAT = 0,
    UNIFORM_VEC2,
    UNIFORM_VEC3,
    UNIFORM_VEC4,
    UNIFORM_INT,
    UNIFORM_IVEC2,
    UNIFORM_IVEC3,
    UNIFORM_IVEC4,
    UNIFORM_SAMPLER2D
} ShaderUniformDataType;

// Material map type
typedef enum {
    MAP_ALBEDO    = 0,       // MAP_DIFFUSE
    MAP_METALNESS = 1,       // MAP_SPECULAR
    MAP_NORMAL    = 2,
    MAP_ROUGHNESS = 3,
    MAP_OCCLUSION,
    MAP_EMISSION,
    MAP_HEIGHT,
    MAP_CUBEMAP,             // NOTE: Uses GL_TEXTURE_CUBE_MAP
    MAP_IRRADIANCE,          // NOTE: Uses GL_TEXTURE_CUBE_MAP
    MAP_PREFILTER,           // NOTE: Uses GL_TEXTURE_CUBE_MAP
    MAP_BRDF
} MaterialMapType;




// Pixel formats
// NOTE: Support depends on OpenGL version and platform
typedef enum {
    UNCOMPRESSED_GRAYSCALE = 1,     // 8 bit per pixel (no alpha)
    UNCOMPRESSED_GRAY_ALPHA,        // 8*2 bpp (2 channels)
    UNCOMPRESSED_R5G6B5,            // 16 bpp
    UNCOMPRESSED_R8G8B8,            // 24 bpp
    UNCOMPRESSED_R5G5B5A1,          // 16 bpp (1 bit alpha)
    UNCOMPRESSED_R4G4B4A4,          // 16 bpp (4 bit alpha)
    UNCOMPRESSED_R8G8B8A8,          // 32 bpp
    UNCOMPRESSED_R32,               // 32 bpp (1 channel - float)
    UNCOMPRESSED_R32G32B32,         // 32*3 bpp (3 channels - float)
    UNCOMPRESSED_R32G32B32A32,      // 32*4 bpp (4 channels - float)
    COMPRESSED_DXT1_RGB,            // 4 bpp (no alpha)
    COMPRESSED_DXT1_RGBA,           // 4 bpp (1 bit alpha)
    COMPRESSED_DXT3_RGBA,           // 8 bpp
    COMPRESSED_DXT5_RGBA,           // 8 bpp
    COMPRESSED_ETC1_RGB,            // 4 bpp
    COMPRESSED_ETC2_RGB,            // 4 bpp
    COMPRESSED_ETC2_EAC_RGBA,       // 8 bpp
    COMPRESSED_PVRT_RGB,            // 4 bpp
    COMPRESSED_PVRT_RGBA,           // 4 bpp
    COMPRESSED_ASTC_4x4_RGBA,       // 8 bpp
    COMPRESSED_ASTC_8x8_RGBA        // 2 bpp
} PixelFormat;

// Texture parameters: filter mode
// NOTE 1: Filtering considers mipmaps if available in the texture
// NOTE 2: Filter is accordingly set for minification and magnification
typedef enum {
    FILTER_POINT = 0,               // No filter, just pixel aproximation
    FILTER_BILINEAR,                // Linear filtering
    FILTER_TRILINEAR,               // Trilinear filtering (linear with mipmaps)
    FILTER_ANISOTROPIC_4X,          // Anisotropic filtering 4x
    FILTER_ANISOTROPIC_8X,          // Anisotropic filtering 8x
    FILTER_ANISOTROPIC_16X,         // Anisotropic filtering 16x
} TextureFilterMode;

// Cubemap layout type
typedef enum {
    CUBEMAP_AUTO_DETECT = 0,        // Automatically detect layout type
    CUBEMAP_LINE_VERTICAL,          // Layout is defined by a vertical line with faces
    CUBEMAP_LINE_HORIZONTAL,        // Layout is defined by an horizontal line with faces
    CUBEMAP_CROSS_THREE_BY_FOUR,    // Layout is defined by a 3x4 cross with cubemap faces
    CUBEMAP_CROSS_FOUR_BY_THREE,    // Layout is defined by a 4x3 cross with cubemap faces
    CUBEMAP_PANORAMA                // Layout is defined by a panorama image (equirectangular map)
} CubemapLayoutType;

// Texture parameters: wrap mode
typedef enum {
    WRAP_REPEAT = 0,        // Repeats texture in tiled mode
    WRAP_CLAMP,             // Clamps texture to edge pixel in tiled mode
    WRAP_MIRROR_REPEAT,     // Mirrors and repeats the texture in tiled mode
    WRAP_MIRROR_CLAMP       // Mirrors and clamps to border the texture in tiled mode
} TextureWrapMode;

// Font type, defines generation method
typedef enum {
    FONT_DEFAULT = 0,       // Default font generation, anti-aliased
    FONT_BITMAP,            // Bitmap font generation, no anti-aliasing
    FONT_SDF                // SDF font generation, requires external shader
} FontType;

// Color blending modes (pre-defined)
typedef enum {
    BLEND_ALPHA = 0,        // Blend textures considering alpha (default)
    BLEND_ADDITIVE,         // Blend textures adding colors
    BLEND_MULTIPLIED        // Blend textures multiplying colors
} BlendMode;

// Gestures type
// NOTE: It could be used as flags to enable only some gestures
typedef enum {
    GESTURE_NONE        = 0,
    GESTURE_TAP         = 1,
    GESTURE_DOUBLETAP   = 2,
    GESTURE_HOLD        = 4,
    GESTURE_DRAG        = 8,
    GESTURE_SWIPE_RIGHT = 16,
    GESTURE_SWIPE_LEFT  = 32,
    GESTURE_SWIPE_UP    = 64,
    GESTURE_SWIPE_DOWN  = 128,
    GESTURE_PINCH_IN    = 256,
    GESTURE_PINCH_OUT   = 512
} GestureType;

// Camera system modes
typedef enum {
    CAMERA_CUSTOM = 0,
    CAMERA_FREE,
    CAMERA_ORBITAL,
    CAMERA_FIRST_PERSON,
    CAMERA_THIRD_PERSON
} CameraMode;

// Camera projection modes
typedef enum {
    CAMERA_PERSPECTIVE = 0,
    CAMERA_ORTHOGRAPHIC
} CameraType;

// Type of n-patch
typedef enum {
    NPT_9PATCH = 0,         // Npatch defined by 3x3 tiles
    NPT_3PATCH_VERTICAL,    // Npatch defined by 1x3 tiles
    NPT_3PATCH_HORIZONTAL   // Npatch defined by 3x1 tiles
} NPatchType;

// Callbacks to be implemented by users
typedef void (*TraceLogCallback)(int logType, const char *text, va_list args);





//------------------------------------------------------------------------------------
// Global Variables Definition
//------------------------------------------------------------------------------------
// It's lonely here...

//------------------------------------------------------------------------------------
// Window and Graphics Device Functions (Module: core)
//------------------------------------------------------------------------------------

// Window-related functions
 void InitWindow(int width, int height, const char *title);  // Initialize window and OpenGL context
 bool WindowShouldClose(void);                               // Check if KEY_ESCAPE pressed or Close icon pressed
 void CloseWindow(void);                                     // Close window and unload OpenGL context
 bool IsWindowReady(void);                                   // Check if window has been initialized successfully
 bool IsWindowMinimized(void);                               // Check if window has been minimized (or lost focus)
 bool IsWindowResized(void);                                 // Check if window has been resized
 bool IsWindowHidden(void);                                  // Check if window is currently hidden
 void ToggleFullscreen(void);                                // Toggle fullscreen mode (only PLATFORM_DESKTOP)
 void UnhideWindow(void);                                    // Show the window
 void HideWindow(void);                                      // Hide the window
 void SetWindowIcon(Image image);                            // Set icon for window (only PLATFORM_DESKTOP)
 void SetWindowTitle(const char *title);                     // Set title for window (only PLATFORM_DESKTOP)
 void SetWindowPosition(int x, int y);                       // Set window position on screen (only PLATFORM_DESKTOP)
 void SetWindowMonitor(int monitor);                         // Set monitor for the current window (fullscreen mode)
 void SetWindowMinSize(int width, int height);               // Set window minimum dimensions (for FLAG_WINDOW_RESIZABLE)
 void SetWindowSize(int width, int height);                  // Set window dimensions
 void *GetWindowHandle(void);                                // Get native window handle
 int GetScreenWidth(void);                                   // Get current screen width
 int GetScreenHeight(void);                                  // Get current screen height
 int GetMonitorCount(void);                                  // Get number of connected monitors
 int GetMonitorWidth(int monitor);                           // Get primary monitor width
 int GetMonitorHeight(int monitor);                          // Get primary monitor height
 int GetMonitorPhysicalWidth(int monitor);                   // Get primary monitor physical width in millimetres
 int GetMonitorPhysicalHeight(int monitor);                  // Get primary monitor physical height in millimetres
 const char *GetMonitorName(int monitor);                    // Get the human-readable, UTF-8 encoded name of the primary monitor
 const char *GetClipboardText(void);                         // Get clipboard text content
 void SetClipboardText(const char *text);                    // Set clipboard text content

// Cursor-related functions
 void ShowCursor(void);                                      // Shows cursor
 void HideCursor(void);                                      // Hides cursor
 bool IsCursorHidden(void);                                  // Check if cursor is not visible
 void EnableCursor(void);                                    // Enables cursor (unlock cursor)
 void DisableCursor(void);                                   // Disables cursor (lock cursor)

// Drawing-related functions
 void ClearBackground(Color color);                          // Set background color (framebuffer clear color)
 void BeginDrawing(void);                                    // Setup canvas (framebuffer) to start drawing
 void EndDrawing(void);                                      // End canvas drawing and swap buffers (double buffering)
 void BeginMode2D(Camera2D camera);                          // Initialize 2D mode with custom camera (2D)
 void EndMode2D(void);                                       // Ends 2D mode with custom camera
 void BeginMode3D(Camera3D camera);                          // Initializes 3D mode with custom camera (3D)
 void EndMode3D(void);                                       // Ends 3D mode and returns to default 2D orthographic mode
 void BeginTextureMode(RenderTexture2D target);              // Initializes render texture for drawing
 void EndTextureMode(void);                                  // Ends drawing to render texture

// Screen-space-related functions
 Ray GetMouseRay(Vector2 mousePosition, Camera camera);      // Returns a ray trace from mouse position
 Vector2 GetWorldToScreen(Vector3 position, Camera camera);  // Returns the screen space position for a 3d world space position
 Matrix GetCameraMatrix(Camera camera);                      // Returns camera transform matrix (view matrix)

// Timing-related functions
 void SetTargetFPS(int fps);                                 // Set target FPS (maximum)
 int GetFPS(void);                                           // Returns current FPS
 float GetFrameTime(void);                                   // Returns time in seconds for last frame drawn
 double GetTime(void);                                       // Returns elapsed time in seconds since InitWindow()

// Color-related functions
 int ColorToInt(Color color);                                // Returns hexadecimal value for a Color
 Vector4 ColorNormalize(Color color);                        // Returns color normalized as float [0..1]
 Vector3 ColorToHSV(Color color);                            // Returns HSV values for a Color
 Color ColorFromHSV(Vector3 hsv);                            // Returns a Color from HSV values
 Color GetColor(int hexValue);                               // Returns a Color struct from hexadecimal value
 Color Fade(Color color, float alpha);                       // Color fade-in or fade-out, alpha goes from 0.0f to 1.0f

// Misc. functions
 void SetConfigFlags(unsigned int flags);                    // Setup window configuration flags (view FLAGS)
 void SetTraceLogLevel(int logType);                         // Set the current threshold (minimum) log level
 void SetTraceLogExit(int logType);                          // Set the exit threshold (minimum) log level
 void SetTraceLogCallback(TraceLogCallback callback);        // Set a trace log callback to enable custom logging
 void TraceLog(int logType, const char *text, ...);          // Show trace log messages (LOG_DEBUG, LOG_INFO, LOG_WARNING, LOG_ERROR)
 void TakeScreenshot(const char *fileName);                  // Takes a screenshot of current screen (saved a .png)
 int GetRandomValue(int min, int max);                       // Returns a random value between min and max (both included)

// Files management functions
 bool FileExists(const char *fileName);                      // Check if file exists
 bool IsFileExtension(const char *fileName, const char *ext);// Check file extension
 bool DirectoryExists(const char *dirPath);                  // Check if a directory path exists
 const char *GetExtension(const char *fileName);             // Get pointer to extension for a filename string
 const char *GetFileName(const char *filePath);              // Get pointer to filename for a path string
 const char *GetFileNameWithoutExt(const char *filePath);    // Get filename string without extension (memory should be freed)
 const char *GetDirectoryPath(const char *fileName);         // Get full path for a given fileName (uses static string)
 const char *GetPrevDirectoryPath(const char *path);         // Get previous directory path for a given path (uses static string)
 const char *GetWorkingDirectory(void);                      // Get current working directory (uses static string)
 char **GetDirectoryFiles(const char *dirPath, int *count);  // Get filenames in a directory path (memory should be freed)
 void ClearDirectoryFiles(void);                             // Clear directory files paths buffers (free memory)
 bool ChangeDirectory(const char *dir);                      // Change working directory, returns true if success
 bool IsFileDropped(void);                                   // Check if a file has been dropped into window
 char **GetDroppedFiles(int *count);                         // Get dropped files names (memory should be freed)
 void ClearDroppedFiles(void);                               // Clear dropped files paths buffer (free memory)
 long GetFileModTime(const char *fileName);                  // Get file modification time (last write time)

// Persistent storage management
 void StorageSaveValue(int position, int value);             // Save integer value to storage file (to defined position)
 int StorageLoadValue(int position);                         // Load integer value from storage file (from defined position)

 void OpenURL(const char *url);                              // Open URL with default system browser (if available)

//------------------------------------------------------------------------------------
// Input Handling Functions (Module: core)
//------------------------------------------------------------------------------------

// Input-related functions: keyboard
 bool IsKeyPressed(int key);                             // Detect if a key has been pressed once
 bool IsKeyDown(int key);                                // Detect if a key is being pressed
 bool IsKeyReleased(int key);                            // Detect if a key has been released once
 bool IsKeyUp(int key);                                  // Detect if a key is NOT being pressed
 int GetKeyPressed(void);                                // Get latest key pressed
 void SetExitKey(int key);                               // Set a custom key to exit program (default is ESC)

// Input-related functions: gamepads
 bool IsGamepadAvailable(int gamepad);                   // Detect if a gamepad is available
 bool IsGamepadName(int gamepad, const char *name);      // Check gamepad name (if available)
 const char *GetGamepadName(int gamepad);                // Return gamepad internal name id
 bool IsGamepadButtonPressed(int gamepad, int button);   // Detect if a gamepad button has been pressed once
 bool IsGamepadButtonDown(int gamepad, int button);      // Detect if a gamepad button is being pressed
 bool IsGamepadButtonReleased(int gamepad, int button);  // Detect if a gamepad button has been released once
 bool IsGamepadButtonUp(int gamepad, int button);        // Detect if a gamepad button is NOT being pressed
 int GetGamepadButtonPressed(void);                      // Get the last gamepad button pressed
 int GetGamepadAxisCount(int gamepad);                   // Return gamepad axis count for a gamepad
 float GetGamepadAxisMovement(int gamepad, int axis);    // Return axis movement value for a gamepad axis

// Input-related functions: mouse
 bool IsMouseButtonPressed(int button);                  // Detect if a mouse button has been pressed once
 bool IsMouseButtonDown(int button);                     // Detect if a mouse button is being pressed
 bool IsMouseButtonReleased(int button);                 // Detect if a mouse button has been released once
 bool IsMouseButtonUp(int button);                       // Detect if a mouse button is NOT being pressed
 int GetMouseX(void);                                    // Returns mouse position X
 int GetMouseY(void);                                    // Returns mouse position Y
 Vector2 GetMousePosition(void);                         // Returns mouse position XY
 void SetMousePosition(int x, int y);                    // Set mouse position XY
 void SetMouseOffset(int offsetX, int offsetY);          // Set mouse offset
 void SetMouseScale(float scaleX, float scaleY);         // Set mouse scaling
 int GetMouseWheelMove(void);                            // Returns mouse wheel movement Y

// Input-related functions: touch
 int GetTouchX(void);                                    // Returns touch position X for touch point 0 (relative to screen size)
 int GetTouchY(void);                                    // Returns touch position Y for touch point 0 (relative to screen size)
 Vector2 GetTouchPosition(int index);                    // Returns touch position XY for a touch point index (relative to screen size)

//------------------------------------------------------------------------------------
// Gestures and Touch Handling Functions (Module: gestures)
//------------------------------------------------------------------------------------
 void SetGesturesEnabled(unsigned int gestureFlags);     // Enable a set of gestures using flags
 bool IsGestureDetected(int gesture);                    // Check if a gesture have been detected
 int GetGestureDetected(void);                           // Get latest detected gesture
 int GetTouchPointsCount(void);                          // Get touch points count
 float GetGestureHoldDuration(void);                     // Get gesture hold time in milliseconds
 Vector2 GetGestureDragVector(void);                     // Get gesture drag vector
 float GetGestureDragAngle(void);                        // Get gesture drag angle
 Vector2 GetGesturePinchVector(void);                    // Get gesture pinch delta
 float GetGesturePinchAngle(void);                       // Get gesture pinch angle

//------------------------------------------------------------------------------------
// Camera System Functions (Module: camera)
//------------------------------------------------------------------------------------
 void SetCameraMode(Camera camera, int mode);                // Set camera mode (multiple camera modes available)
 void UpdateCamera(Camera *camera);                          // Update camera position for selected mode

 void SetCameraPanControl(int panKey);                       // Set camera pan key to combine with mouse movement (free camera)
 void SetCameraAltControl(int altKey);                       // Set camera alt key to combine with mouse movement (free camera)
 void SetCameraSmoothZoomControl(int szKey);                 // Set camera smooth zoom key to combine with mouse (free camera)
 void SetCameraMoveControls(int frontKey, int backKey, int rightKey, int leftKey, int upKey, int downKey); // Set camera move controls (1st person and 3rd person cameras)

//------------------------------------------------------------------------------------
// Basic Shapes Drawing Functions (Module: shapes)
//------------------------------------------------------------------------------------

// Basic shapes drawing functions
 void DrawPixel(int posX, int posY, Color color);                                                   // Draw a pixel
 void DrawPixelV(Vector2 position, Color color);                                                    // Draw a pixel (Vector version)
 void DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, Color color);                // Draw a line
 void DrawLineV(Vector2 startPos, Vector2 endPos, Color color);                                     // Draw a line (Vector version)
 void DrawLineEx(Vector2 startPos, Vector2 endPos, float thick, Color color);                       // Draw a line defining thickness
 void DrawLineBezier(Vector2 startPos, Vector2 endPos, float thick, Color color);                   // Draw a line using cubic-bezier curves in-out
 void DrawLineStrip(Vector2 *points, int numPoints, Color color);                                   // Draw lines sequence
 void DrawCircle(int centerX, int centerY, float radius, Color color);                              // Draw a color-filled circle
 void DrawCircleSector(Vector2 center, float radius, int startAngle, int endAngle, int segments, Color color);     // Draw a piece of a circle
 void DrawCircleSectorLines(Vector2 center, float radius, int startAngle, int endAngle, int segments, Color color);    // Draw circle sector outline
 void DrawCircleGradient(int centerX, int centerY, float radius, Color color1, Color color2);       // Draw a gradient-filled circle
 void DrawCircleV(Vector2 center, float radius, Color color);                                       // Draw a color-filled circle (Vector version)
 void DrawCircleLines(int centerX, int centerY, float radius, Color color);                         // Draw circle outline
 void DrawRing(Vector2 center, float innerRadius, float outerRadius, int startAngle, int endAngle, int segments, Color color); // Draw ring
 void DrawRingLines(Vector2 center, float innerRadius, float outerRadius, int startAngle, int endAngle, int segments, Color color);    // Draw ring outline
 void DrawRectangle(int posX, int posY, int width, int height, Color color);                        // Draw a color-filled rectangle
 void DrawRectangleV(Vector2 position, Vector2 size, Color color);                                  // Draw a color-filled rectangle (Vector version)
 void DrawRectangleRec(Rectangle rec, Color color);                                                 // Draw a color-filled rectangle
 void DrawRectanglePro(Rectangle rec, Vector2 origin, float rotation, Color color);                 // Draw a color-filled rectangle with pro parameters
 void DrawRectangleGradientV(int posX, int posY, int width, int height, Color color1, Color color2);// Draw a vertical-gradient-filled rectangle
 void DrawRectangleGradientH(int posX, int posY, int width, int height, Color color1, Color color2);// Draw a horizontal-gradient-filled rectangle
 void DrawRectangleGradientEx(Rectangle rec, Color col1, Color col2, Color col3, Color col4);       // Draw a gradient-filled rectangle with custom vertex colors
 void DrawRectangleLines(int posX, int posY, int width, int height, Color color);                   // Draw rectangle outline
 void DrawRectangleLinesEx(Rectangle rec, int lineThick, Color color);                              // Draw rectangle outline with extended parameters
 void DrawRectangleRounded(Rectangle rec, float roundness, int segments, Color color);              // Draw rectangle with rounded edges
 void DrawRectangleRoundedLines(Rectangle rec, float roundness, int segments, int lineThick, Color color); // Draw rectangle with rounded edges outline
 void DrawTriangle(Vector2 v1, Vector2 v2, Vector2 v3, Color color);                                // Draw a color-filled triangle
 void DrawTriangleLines(Vector2 v1, Vector2 v2, Vector2 v3, Color color);                           // Draw triangle outline
 void DrawTriangleFan(Vector2 *points, int numPoints, Color color);                                 // Draw a triangle fan defined by points
 void DrawTriangleStrip(Vector2 *points, int pointsCount, Color color);                             // Draw a triangle strip defined by points
 void DrawPoly(Vector2 center, int sides, float radius, float rotation, Color color);               // Draw a regular polygon (Vector version)

 void SetShapesTexture(Texture2D texture, Rectangle source);                                        // Define default texture used to draw shapes

// Basic shapes collision detection functions
 bool CheckCollisionRecs(Rectangle rec1, Rectangle rec2);                                           // Check collision between two rectangles
 bool CheckCollisionCircles(Vector2 center1, float radius1, Vector2 center2, float radius2);        // Check collision between two circles
 bool CheckCollisionCircleRec(Vector2 center, float radius, Rectangle rec);                         // Check collision between circle and rectangle
 Rectangle GetCollisionRec(Rectangle rec1, Rectangle rec2);                                         // Get collision rectangle for two rectangles collision
 bool CheckCollisionPointRec(Vector2 point, Rectangle rec);                                         // Check if point is inside rectangle
 bool CheckCollisionPointCircle(Vector2 point, Vector2 center, float radius);                       // Check if point is inside circle
 bool CheckCollisionPointTriangle(Vector2 point, Vector2 p1, Vector2 p2, Vector2 p3);               // Check if point is inside a triangle

//------------------------------------------------------------------------------------
// Texture Loading and Drawing Functions (Module: textures)
//------------------------------------------------------------------------------------

// Image/Texture2D data loading/unloading/saving functions
 Image LoadImage(const char *fileName);                                                             // Load image from file into CPU memory (RAM)
 Image LoadImageEx(Color *pixels, int width, int height);                                           // Load image from Color array data (RGBA - 32bit)
 Image LoadImagePro(void *data, int width, int height, int format);                                 // Load image from raw data with parameters
 Image LoadImageRaw(const char *fileName, int width, int height, int format, int headerSize);       // Load image from RAW file data
 void ExportImage(Image image, const char *fileName);                                               // Export image data to file
 void ExportImageAsCode(Image image, const char *fileName);                                         // Export image as code file defining an array of bytes
 Texture2D LoadTexture(const char *fileName);                                                       // Load texture from file into GPU memory (VRAM)
 Texture2D LoadTextureFromImage(Image image);                                                       // Load texture from image data
 TextureCubemap LoadTextureCubemap(Image image, int layoutType);                                    // Load cubemap from image, multiple image cubemap layouts supported
 RenderTexture2D LoadRenderTexture(int width, int height);                                          // Load texture for rendering (framebuffer)
 void UnloadImage(Image image);                                                                     // Unload image from CPU memory (RAM)
 void UnloadTexture(Texture2D texture);                                                             // Unload texture from GPU memory (VRAM)
 void UnloadRenderTexture(RenderTexture2D target);                                                  // Unload render texture from GPU memory (VRAM)
 Color *GetImageData(Image image);                                                                  // Get pixel data from image as a Color struct array
 Vector4 *GetImageDataNormalized(Image image);                                                      // Get pixel data from image as Vector4 array (float normalized)
 Rectangle GetImageAlphaBorder(Image image, float threshold);                                       // Get image alpha border rectangle
 int GetPixelDataSize(int width, int height, int format);                                           // Get pixel data size in bytes (image or texture)
 Image GetTextureData(Texture2D texture);                                                           // Get pixel data from GPU texture and return an Image
 Image GetScreenData(void);                                                                         // Get pixel data from screen buffer and return an Image (screenshot)
 void UpdateTexture(Texture2D texture, const void *pixels);                                         // Update GPU texture with new data

// Image manipulation functions
 Image ImageCopy(Image image);                                                                      // Create an image duplicate (useful for transformations)
 Image ImageFromImage(Image image, Rectangle rec);                                                  // Create an image from another image piece
 void ImageToPOT(Image *image, Color fillColor);                                                    // Convert image to POT (power-of-two)
 void ImageFormat(Image *image, int newFormat);                                                     // Convert image data to desired format
 void ImageAlphaMask(Image *image, Image alphaMask);                                                // Apply alpha mask to image
 void ImageAlphaClear(Image *image, Color color, float threshold);                                  // Clear alpha channel to desired color
 void ImageAlphaCrop(Image *image, float threshold);                                                // Crop image depending on alpha value
 void ImageAlphaPremultiply(Image *image);                                                          // Premultiply alpha channel
 void ImageCrop(Image *image, Rectangle crop);                                                      // Crop an image to a defined rectangle
 void ImageResize(Image *image, int newWidth, int newHeight);                                       // Resize image (Bicubic scaling algorithm)
 void ImageResizeNN(Image *image, int newWidth,int newHeight);                                      // Resize image (Nearest-Neighbor scaling algorithm)
 void ImageResizeCanvas(Image *image, int newWidth, int newHeight, int offsetX, int offsetY, Color color);  // Resize canvas and fill with color
 void ImageMipmaps(Image *image);                                                                   // Generate all mipmap levels for a provided image
 void ImageDither(Image *image, int rBpp, int gBpp, int bBpp, int aBpp);                            // Dither image data to 16bpp or lower (Floyd-Steinberg dithering)
 Color *ImageExtractPalette(Image image, int maxPaletteSize, int *extractCount);                    // Extract color palette from image to maximum size (memory should be freed)
 Image ImageText(const char *text, int fontSize, Color color);                                      // Create an image from text (default font)
 Image ImageTextEx(Font font, const char *text, float fontSize, float spacing, Color tint);         // Create an image from text (custom sprite font)
 void ImageDraw(Image *dst, Image src, Rectangle srcRec, Rectangle dstRec, Color tint);             // Draw a source image within a destination image (tint applied to source)
 void ImageDrawRectangle(Image *dst, Rectangle rec, Color color);                                   // Draw rectangle within an image
 void ImageDrawRectangleLines(Image *dst, Rectangle rec, int thick, Color color);                   // Draw rectangle lines within an image
 void ImageDrawText(Image *dst, Vector2 position, const char *text, int fontSize, Color color);     // Draw text (default font) within an image (destination)
 void ImageDrawTextEx(Image *dst, Vector2 position, Font font, const char *text, float fontSize, float spacing, Color color); // Draw text (custom sprite font) within an image (destination)
 void ImageFlipVertical(Image *image);                                                              // Flip image vertically
 void ImageFlipHorizontal(Image *image);                                                            // Flip image horizontally
 void ImageRotateCW(Image *image);                                                                  // Rotate image clockwise 90deg
 void ImageRotateCCW(Image *image);                                                                 // Rotate image counter-clockwise 90deg
 void ImageColorTint(Image *image, Color color);                                                    // Modify image color: tint
 void ImageColorInvert(Image *image);                                                               // Modify image color: invert
 void ImageColorGrayscale(Image *image);                                                            // Modify image color: grayscale
 void ImageColorContrast(Image *image, float contrast);                                             // Modify image color: contrast (-100 to 100)
 void ImageColorBrightness(Image *image, int brightness);                                           // Modify image color: brightness (-255 to 255)
 void ImageColorReplace(Image *image, Color color, Color replace);                                  // Modify image color: replace color

// Image generation functions
 Image GenImageColor(int width, int height, Color color);                                           // Generate image: plain color
 Image GenImageGradientV(int width, int height, Color top, Color bottom);                           // Generate image: vertical gradient
 Image GenImageGradientH(int width, int height, Color left, Color right);                           // Generate image: horizontal gradient
 Image GenImageGradientRadial(int width, int height, float density, Color inner, Color outer);      // Generate image: radial gradient
 Image GenImageChecked(int width, int height, int checksX, int checksY, Color col1, Color col2);    // Generate image: checked
 Image GenImageWhiteNoise(int width, int height, float factor);                                     // Generate image: white noise
 Image GenImagePerlinNoise(int width, int height, int offsetX, int offsetY, float scale);           // Generate image: perlin noise
 Image GenImageCellular(int width, int height, int tileSize);                                       // Generate image: cellular algorithm. Bigger tileSize means bigger cells

// Texture2D configuration functions
 void GenTextureMipmaps(Texture2D *texture);                                                        // Generate GPU mipmaps for a texture
 void SetTextureFilter(Texture2D texture, int filterMode);                                          // Set texture scaling filter mode
 void SetTextureWrap(Texture2D texture, int wrapMode);                                              // Set texture wrapping mode

// Texture2D drawing functions
 void DrawTexture(Texture2D texture, int posX, int posY, Color tint);                               // Draw a Texture2D
 void DrawTextureV(Texture2D texture, Vector2 position, Color tint);                                // Draw a Texture2D with position defined as Vector2
 void DrawTextureEx(Texture2D texture, Vector2 position, float rotation, float scale, Color tint);  // Draw a Texture2D with extended parameters
 void DrawTextureRec(Texture2D texture, Rectangle sourceRec, Vector2 position, Color tint);         // Draw a part of a texture defined by a rectangle
 void DrawTextureQuad(Texture2D texture, Vector2 tiling, Vector2 offset, Rectangle quad, Color tint);  // Draw texture quad with tiling and offset parameters
 void DrawTexturePro(Texture2D texture, Rectangle sourceRec, Rectangle destRec, Vector2 origin, float rotation, Color tint);       // Draw a part of a texture defined by a rectangle with 'pro' parameters
 void DrawTextureNPatch(Texture2D texture, NPatchInfo nPatchInfo, Rectangle destRec, Vector2 origin, float rotation, Color tint);  // Draws a texture (or part of it) that stretches or shrinks nicely

//------------------------------------------------------------------------------------
// Font Loading and Text Drawing Functions (Module: text)
//------------------------------------------------------------------------------------

// Font loading/unloading functions
 Font GetFontDefault(void);                                                            // Get the default Font
 Font LoadFont(const char *fileName);                                                  // Load font from file into GPU memory (VRAM)
 Font LoadFontEx(const char *fileName, int fontSize, int *fontChars, int charsCount);  // Load font from file with extended parameters
 Font LoadFontFromImage(Image image, Color key, int firstChar);                        // Load font from Image (XNA style)
 CharInfo *LoadFontData(const char *fileName, int fontSize, int *fontChars, int charsCount, int type); // Load font data for further use
 Image GenImageFontAtlas(const CharInfo *chars, Rectangle **recs, int charsCount, int fontSize, int padding, int packMethod);  // Generate image font atlas using chars info
 void UnloadFont(Font font);                                                           // Unload Font from GPU memory (VRAM)

// Text drawing functions
 void DrawFPS(int posX, int posY);                                                     // Shows current FPS
 void DrawText(const char *text, int posX, int posY, int fontSize, Color color);       // Draw text (using default font)
 void DrawTextEx(Font font, const char *text, Vector2 position, float fontSize, float spacing, Color tint);                // Draw text using font and additional parameters
 void DrawTextRec(Font font, const char *text, Rectangle rec, float fontSize, float spacing, bool wordWrap, Color tint);   // Draw text using font inside rectangle limits
 void DrawTextRecEx(Font font, const char *text, Rectangle rec, float fontSize, float spacing, bool wordWrap, Color tint,
                         int selectStart, int selectLength, Color selectText, Color selectBack);    // Draw text using font inside rectangle limits with support for text selection

// Text misc. functions
 int MeasureText(const char *text, int fontSize);                                      // Measure string width for default font
 Vector2 MeasureTextEx(Font font, const char *text, float fontSize, float spacing);    // Measure string size for Font
 int GetGlyphIndex(Font font, int character);                                          // Get index position for a unicode character on font
 int GetNextCodepoint(const char *text, int *bytesProcessed);                          // Returns next codepoint in a UTF8 encoded string
                                                                                            // NOTE: 0x3f('?') is returned on failure

// Text strings management functions
// NOTE: Some strings allocate memory internally for returned strings, just be careful!
 bool TextIsEqual(const char *text1, const char *text2);                               // Check if two text string are equal
 unsigned int TextLength(const char *text);                                            // Get text length, checks for '\0' ending
 unsigned int TextCountCodepoints(const char *text);                                   // Get total number of characters (codepoints) in a UTF8 encoded string
 const char *TextFormat(const char *text, ...);                                        // Text formatting with variables (sprintf style)
 const char *TextSubtext(const char *text, int position, int length);                  // Get a piece of a text string
 char *TextReplace(char *text, const char *replace, const char *by);                   // Replace text string (memory should be freed!)
 char *TextInsert(const char *text, const char *insert, int position);                 // Insert text in a position (memory should be freed!)
 const char *TextJoin(const char **textList, int count, const char *delimiter);        // Join text strings with delimiter
 const char **TextSplit(const char *text, char delimiter, int *count);                 // Split text into multiple strings
 void TextAppend(char *text, const char *append, int *position);                       // Append text at specific position and move cursor!
 int TextFindIndex(const char *text, const char *find);                                // Find first text occurrence within a string
 const char *TextToUpper(const char *text);                      // Get upper case version of provided string
 const char *TextToLower(const char *text);                      // Get lower case version of provided string
 const char *TextToPascal(const char *text);                     // Get Pascal case notation version of provided string
 int TextToInteger(const char *text);                            // Get integer value from text (negative values not supported)

//------------------------------------------------------------------------------------
// Basic 3d Shapes Drawing Functions (Module: models)
//------------------------------------------------------------------------------------

// Basic geometric 3D shapes drawing functions
 void DrawLine3D(Vector3 startPos, Vector3 endPos, Color color);                                    // Draw a line in 3D world space
 void DrawCircle3D(Vector3 center, float radius, Vector3 rotationAxis, float rotationAngle, Color color); // Draw a circle in 3D world space
 void DrawCube(Vector3 position, float width, float height, float length, Color color);             // Draw cube
 void DrawCubeV(Vector3 position, Vector3 size, Color color);                                       // Draw cube (Vector version)
 void DrawCubeWires(Vector3 position, float width, float height, float length, Color color);        // Draw cube wires
 void DrawCubeWiresV(Vector3 position, Vector3 size, Color color);                                  // Draw cube wires (Vector version)
 void DrawCubeTexture(Texture2D texture, Vector3 position, float width, float height, float length, Color color); // Draw cube textured
 void DrawSphere(Vector3 centerPos, float radius, Color color);                                     // Draw sphere
 void DrawSphereEx(Vector3 centerPos, float radius, int rings, int slices, Color color);            // Draw sphere with extended parameters
 void DrawSphereWires(Vector3 centerPos, float radius, int rings, int slices, Color color);         // Draw sphere wires
 void DrawCylinder(Vector3 position, float radiusTop, float radiusBottom, float height, int slices, Color color); // Draw a cylinder/cone
 void DrawCylinderWires(Vector3 position, float radiusTop, float radiusBottom, float height, int slices, Color color); // Draw a cylinder/cone wires
 void DrawPlane(Vector3 centerPos, Vector2 size, Color color);                                      // Draw a plane XZ
 void DrawRay(Ray ray, Color color);                                                                // Draw a ray line
 void DrawGrid(int slices, float spacing);                                                          // Draw a grid (centered at (0, 0, 0))
 void DrawGizmo(Vector3 position);                                                                  // Draw simple gizmo
//DrawTorus(), DrawTeapot() could be useful?

//------------------------------------------------------------------------------------
// Model 3d Loading and Drawing Functions (Module: models)
//------------------------------------------------------------------------------------

// Model loading/unloading functions
 Model LoadModel(const char *fileName);                                                            // Load model from files (meshes and materials)
 Model LoadModelFromMesh(Mesh mesh);                                                               // Load model from generated mesh (default material)
 void UnloadModel(Model model);                                                                    // Unload model from memory (RAM and/or VRAM)

// Mesh loading/unloading functions
 Mesh *LoadMeshes(const char *fileName, int *meshCount);                                           // Load meshes from model file
 void ExportMesh(Mesh mesh, const char *fileName);                                                 // Export mesh data to file
 void UnloadMesh(Mesh mesh);                                                                       // Unload mesh from memory (RAM and/or VRAM)

// Material loading/unloading functions
 Material *LoadMaterials(const char *fileName, int *materialCount);                                // Load materials from model file
 Material LoadMaterialDefault(void);                                                               // Load default material (Supports: DIFFUSE, SPECULAR, NORMAL maps)
 void UnloadMaterial(Material material);                                                           // Unload material from GPU memory (VRAM)
 void SetMaterialTexture(Material *material, int mapType, Texture2D texture);                      // Set texture for a material map type (MAP_DIFFUSE, MAP_SPECULAR...)
 void SetModelMeshMaterial(Model *model, int meshId, int materialId);                              // Set material for a mesh

// Model animations loading/unloading functions
 ModelAnimation *LoadModelAnimations(const char *fileName, int *animsCount);                       // Load model animations from file
 void UpdateModelAnimation(Model model, ModelAnimation anim, int frame);                           // Update model animation pose
 void UnloadModelAnimation(ModelAnimation anim);                                                   // Unload animation data
 bool IsModelAnimationValid(Model model, ModelAnimation anim);                                     // Check model animation skeleton match

// Mesh generation functions
 Mesh GenMeshPoly(int sides, float radius);                                                        // Generate polygonal mesh
 Mesh GenMeshPlane(float width, float length, int resX, int resZ);                                 // Generate plane mesh (with subdivisions)
 Mesh GenMeshCube(float width, float height, float length);                                        // Generate cuboid mesh
 Mesh GenMeshSphere(float radius, int rings, int slices);                                          // Generate sphere mesh (standard sphere)
 Mesh GenMeshHemiSphere(float radius, int rings, int slices);                                      // Generate half-sphere mesh (no bottom cap)
 Mesh GenMeshCylinder(float radius, float height, int slices);                                     // Generate cylinder mesh
 Mesh GenMeshTorus(float radius, float size, int radSeg, int sides);                               // Generate torus mesh
 Mesh GenMeshKnot(float radius, float size, int radSeg, int sides);                                // Generate trefoil knot mesh
 Mesh GenMeshHeightmap(Image heightmap, Vector3 size);                                             // Generate heightmap mesh from image data
 Mesh GenMeshCubicmap(Image cubicmap, Vector3 cubeSize);                                           // Generate cubes-based map mesh from image data

// Mesh manipulation functions
 BoundingBox MeshBoundingBox(Mesh mesh);                                                           // Compute mesh bounding box limits
 void MeshTangents(Mesh *mesh);                                                                    // Compute mesh tangents
 void MeshBinormals(Mesh *mesh);                                                                   // Compute mesh binormals

// Model drawing functions
 void DrawModel(Model model, Vector3 position, float scale, Color tint);                           // Draw a model (with texture if set)
 void DrawModelEx(Model model, Vector3 position, Vector3 rotationAxis, float rotationAngle, Vector3 scale, Color tint); // Draw a model with extended parameters
 void DrawModelWires(Model model, Vector3 position, float scale, Color tint);                      // Draw a model wires (with texture if set)
 void DrawModelWiresEx(Model model, Vector3 position, Vector3 rotationAxis, float rotationAngle, Vector3 scale, Color tint); // Draw a model wires (with texture if set) with extended parameters
 void DrawBoundingBox(BoundingBox box, Color color);                                               // Draw bounding box (wires)
 void DrawBillboard(Camera camera, Texture2D texture, Vector3 center, float size, Color tint);     // Draw a billboard texture
 void DrawBillboardRec(Camera camera, Texture2D texture, Rectangle sourceRec, Vector3 center, float size, Color tint); // Draw a billboard texture defined by sourceRec

// Collision detection functions
 bool CheckCollisionSpheres(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB);       // Detect collision between two spheres
 bool CheckCollisionBoxes(BoundingBox box1, BoundingBox box2);                                     // Detect collision between two bounding boxes
 bool CheckCollisionBoxSphere(BoundingBox box, Vector3 center, float radius);                      // Detect collision between box and sphere
 bool CheckCollisionRaySphere(Ray ray, Vector3 center, float radius);                              // Detect collision between ray and sphere
 bool CheckCollisionRaySphereEx(Ray ray, Vector3 center, float radius, Vector3 *collisionPoint);   // Detect collision between ray and sphere, returns collision point
 bool CheckCollisionRayBox(Ray ray, BoundingBox box);                                              // Detect collision between ray and box
 RayHitInfo GetCollisionRayModel(Ray ray, Model model);                                            // Get collision info between ray and model
 RayHitInfo GetCollisionRayTriangle(Ray ray, Vector3 p1, Vector3 p2, Vector3 p3);                  // Get collision info between ray and triangle
 RayHitInfo GetCollisionRayGround(Ray ray, float groundHeight);                                    // Get collision info between ray and ground plane (Y-normal plane)

//------------------------------------------------------------------------------------
// Shaders System Functions (Module: rlgl)
// NOTE: This functions are useless when using OpenGL 1.1
//------------------------------------------------------------------------------------

// Shader loading/unloading functions
 char *LoadText(const char *fileName);                               // Load chars array from text file
 Shader LoadShader(const char *vsFileName, const char *fsFileName);  // Load shader from files and bind default locations
 Shader LoadShaderCode(char *vsCode, char *fsCode);                  // Load shader from code strings and bind default locations
 void UnloadShader(Shader shader);                                   // Unload shader from GPU memory (VRAM)

 Shader GetShaderDefault(void);                                      // Get default shader
 Texture2D GetTextureDefault(void);                                  // Get default texture

// Shader configuration functions
 int GetShaderLocation(Shader shader, const char *uniformName);      // Get shader uniform location
 void SetShaderValue(Shader shader, int uniformLoc, const void *value, int uniformType);               // Set shader uniform value
 void SetShaderValueV(Shader shader, int uniformLoc, const void *value, int uniformType, int count);   // Set shader uniform value vector
 void SetShaderValueMatrix(Shader shader, int uniformLoc, Matrix mat);         // Set shader uniform value (matrix 4x4)
 void SetShaderValueTexture(Shader shader, int uniformLoc, Texture2D texture); // Set shader uniform value for texture
 void SetMatrixProjection(Matrix proj);                              // Set a custom projection matrix (replaces internal projection matrix)
 void SetMatrixModelview(Matrix view);                               // Set a custom modelview matrix (replaces internal modelview matrix)
 Matrix GetMatrixModelview(void);                                    // Get internal modelview matrix

// Texture maps generation (PBR)
// NOTE: Required shaders should be provided
 Texture2D GenTextureCubemap(Shader shader, Texture2D skyHDR, int size);       // Generate cubemap texture from HDR texture
 Texture2D GenTextureIrradiance(Shader shader, Texture2D cubemap, int size);   // Generate irradiance texture using cubemap data
 Texture2D GenTexturePrefilter(Shader shader, Texture2D cubemap, int size);    // Generate prefilter texture using cubemap data
 Texture2D GenTextureBRDF(Shader shader, int size);                  // Generate BRDF texture

// Shading begin/end functions
 void BeginShaderMode(Shader shader);                                // Begin custom shader drawing
 void EndShaderMode(void);                                           // End custom shader drawing (use default shader)
 void BeginBlendMode(int mode);                                      // Begin blending mode (alpha, additive, multiplied)
 void EndBlendMode(void);                                            // End blending mode (reset to default: alpha blending)
 void BeginScissorMode(int x, int y, int width, int height);         // Begin scissor mode (define screen area for following drawing)
 void EndScissorMode(void);                                          // End scissor mode

// VR control functions
 void InitVrSimulator(void);                       // Init VR simulator for selected device parameters
 void CloseVrSimulator(void);                      // Close VR simulator for current device
 void UpdateVrTracking(Camera *camera);            // Update VR tracking (position and orientation) and camera
 void SetVrConfiguration(VrDeviceInfo info, Shader distortion);      // Set stereo rendering configuration parameters
 bool IsVrSimulatorReady(void);                    // Detect if VR simulator is ready
 void ToggleVrMode(void);                          // Enable/Disable VR experience
 void BeginVrDrawing(void);                        // Begin VR simulator stereo rendering
 void EndVrDrawing(void);                          // End VR simulator stereo rendering

//------------------------------------------------------------------------------------
// Audio Loading and Playing Functions (Module: audio)
//------------------------------------------------------------------------------------

// Audio device management functions
 void InitAudioDevice(void);                                     // Initialize audio device and context
 void CloseAudioDevice(void);                                    // Close the audio device and context
 bool IsAudioDeviceReady(void);                                  // Check if audio device has been initialized successfully
 void SetMasterVolume(float volume);                             // Set master volume (listener)

// Wave/Sound loading/unloading functions
 Wave LoadWave(const char *fileName);                            // Load wave data from file
 Sound LoadSound(const char *fileName);                          // Load sound from file
 Sound LoadSoundFromWave(Wave wave);                             // Load sound from wave data
 void UpdateSound(Sound sound, const void *data, int samplesCount);// Update sound buffer with new data
 void UnloadWave(Wave wave);                                     // Unload wave data
 void UnloadSound(Sound sound);                                  // Unload sound
 void ExportWave(Wave wave, const char *fileName);               // Export wave data to file
 void ExportWaveAsCode(Wave wave, const char *fileName);         // Export wave sample data to code (.h)

// Wave/Sound management functions
 void PlaySound(Sound sound);                                    // Play a sound
 void StopSound(Sound sound);                                    // Stop playing a sound
 void PauseSound(Sound sound);                                   // Pause a sound
 void ResumeSound(Sound sound);                                  // Resume a paused sound
 void PlaySoundMulti(Sound sound);                               // Play a sound (using multichannel buffer pool)
 void StopSoundMulti(void);                                      // Stop any sound playing (using multichannel buffer pool)
 int GetSoundsPlaying(void);                                     // Get number of sounds playing in the multichannel
 bool IsSoundPlaying(Sound sound);                               // Check if a sound is currently playing
 void SetSoundVolume(Sound sound, float volume);                 // Set volume for a sound (1.0 is max level)
 void SetSoundPitch(Sound sound, float pitch);                   // Set pitch for a sound (1.0 is base level)
 void WaveFormat(Wave *wave, int sampleRate, int sampleSize, int channels);  // Convert wave data to desired format
 Wave WaveCopy(Wave wave);                                       // Copy a wave to a new wave
 void WaveCrop(Wave *wave, int initSample, int finalSample);     // Crop a wave to defined samples range
 float *GetWaveData(Wave wave);                                  // Get samples data from wave as a floats array

// Music management functions
 Music LoadMusicStream(const char *fileName);                    // Load music stream from file
 void UnloadMusicStream(Music music);                            // Unload music stream
 void PlayMusicStream(Music music);                              // Start music playing
 void UpdateMusicStream(Music music);                            // Updates buffers for music streaming
 void StopMusicStream(Music music);                              // Stop music playing
 void PauseMusicStream(Music music);                             // Pause music playing
 void ResumeMusicStream(Music music);                            // Resume playing paused music
 bool IsMusicPlaying(Music music);                               // Check if music is playing
 void SetMusicVolume(Music music, float volume);                 // Set volume for music (1.0 is max level)
 void SetMusicPitch(Music music, float pitch);                   // Set pitch for a music (1.0 is base level)
 void SetMusicLoopCount(Music music, int count);                 // Set music loop count (loop repeats)
 float GetMusicTimeLength(Music music);                          // Get music time length (in seconds)
 float GetMusicTimePlayed(Music music);                          // Get current music time played (in seconds)

// AudioStream management functions
 AudioStream InitAudioStream(unsigned int sampleRate, unsigned int sampleSize, unsigned int channels); // Init audio stream (to stream raw audio pcm data)
 void UpdateAudioStream(AudioStream stream, const void *data, int samplesCount); // Update audio stream buffers with data
 void CloseAudioStream(AudioStream stream);                      // Close audio stream and free memory
 bool IsAudioBufferProcessed(AudioStream stream);                // Check if any audio stream buffers requires refill
 void PlayAudioStream(AudioStream stream);                       // Play audio stream
 void PauseAudioStream(AudioStream stream);                      // Pause audio stream
 void ResumeAudioStream(AudioStream stream);                     // Resume audio stream
 bool IsAudioStreamPlaying(AudioStream stream);                  // Check if audio stream is playing
 void StopAudioStream(AudioStream stream);                       // Stop audio stream
 void SetAudioStreamVolume(AudioStream stream, float volume);    // Set volume for audio stream (1.0 is max level)
 void SetAudioStreamPitch(AudioStream stream, float pitch);      // Set pitch for audio stream (1.0 is base level)

//------------------------------------------------------------------------------------
// Network (Module: network)
//------------------------------------------------------------------------------------

// IN PROGRESS: Check rnet.h for reference










/**********************************************************************************************
*
*   raymath v1.2 - Math functions to work with Vector3, Matrix and Quaternions
*
*   CONFIGURATION:
*
*   #define RAYMATH_IMPLEMENTATION
*       Generates the implementation of the library into the included file.
*       If not defined, the library is in header only mode and can be included in other headers
*       or source files without problems. But only ONE file should hold the implementation.
*
*   #define RAYMATH_HEADER_ONLY
*       Define static inline functions code, so #include header suffices for use.
*       This may use up lots of memory.
*
*   #define RAYMATH_STANDALONE
*       Avoid raylib.h header inclusion in this file.
*       Vector3 and Matrix data types are defined internally in raymath module.
*
*
*   LICENSE: zlib/libpng
*
*   Copyright (c) 2015-2019 Ramon Santamaria (@raysan5)
*
*   This software is provided "as-is", without any express or implied warranty. In no event
*   will the authors be held liable for any damages arising from the use of this software.
*
*   Permission is granted to anyone to use this software for any purpose, including commercial
*   applications, and to alter it and redistribute it freely, subject to the following restrictions:
*
*     1. The origin of this software must not be misrepresented; you must not claim that you
*     wrote the original software. If you use this software in a product, an acknowledgment
*     in the product documentation would be appreciated but is not required.
*
*     2. Altered source versions must be plainly marked as such, and must not be misrepresented
*     as being the original software.
*
*     3. This notice may not be removed or altered from any source distribution.
*
**********************************************************************************************/




//#define RAYMATH_STANDALONE      // NOTE: To use raymath as standalone lib, just uncomment this line
//#define RAYMATH_HEADER_ONLY     // NOTE: To compile functions as static inline, uncomment this line


    

















    


        
    


//----------------------------------------------------------------------------------
// Defines and Macros
//----------------------------------------------------------------------------------












// Return float vector for Matrix

    


// Return float vector for Vector3

    


//----------------------------------------------------------------------------------
// Types and Structures Definition
//----------------------------------------------------------------------------------
































// NOTE: Helper types to be used instead of array return types for *ToFloat functions
typedef struct float3 { float v[3]; } float3;
typedef struct float16 { float v[16]; } float16;


//
// math.h
//
//      Copyright (c) Microsoft Corporation. All rights reserved.
//
// The C Standard Library <math.h> header.  This header consists of two parts:
// <corecrt_math.h> contains the math library; <corecrt_math_defines.h> contains
// the nonstandard but useful constant definitions.  The headers are divided in
// this way for modularity (to support the C++ modules feature).
//

//
// corecrt_math.h
//
//      Copyright (c) Microsoft Corporation. All rights reserved.
//
// The majority of the C Standard Library <math.h> functionality.
//
#pragma once




//
// corecrt.h
//
//      Copyright (c) Microsoft Corporation. All rights reserved.
//
// Declarations used throughout the CoreCRT library.
//
#pragma once



__pragma(pack(push, 8))



//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Annotation Macros
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

    




        
    


// If you need the ability to remove __declspec(import) from an API, to support static replacement,
// declare the API using _ACRTIMP_ALT instead of _ACRTIMP.

    



    




        
    





    



    









    


// __declspec(guard(overflow)) enabled by /sdl compiler switch for CRT allocators



    





    


// The CLR requires code calling other SecurityCritical code or using SecurityCritical types
// to be marked as SecurityCritical.
// _CRT_SECURITYCRITICAL_ATTRIBUTE covers this for internal function definitions.
// _CRT_INLINE_PURE_SECURITYCRITICAL_ATTRIBUTE is for inline pure functions defined in the header.
// This is clr:pure-only because for mixed mode we compile inline functions as native.



    














    


        
    





    





    





    




//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Miscellaneous Stuff
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+





















    typedef _Bool __crt_bool;











    










// CRT headers are included into some kinds of source files where only data type
// definitions and macro definitions are required but function declarations and
// inline function definitions are not.  These files include assembly files, IDL
// files, and resource files.  The tools that process these files often have a
// limited ability to process C and C++ code.  The _CRT_FUNCTIONS_REQUIRED macro
// is defined to 1 when we are compiling a file that actually needs functions to
// be declared (and defined, where applicable), and to 0 when we are compiling a
// file that does not.  This allows us to suppress declarations and definitions
// that are not compilable with the aforementioned tools.

    


        
    







    



 






  


   
  
 


//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Windows API Partitioning and ARM Desktop Support
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

    











        
    



    



    
        
    




// Verify that the ARM Desktop SDK is available when building an ARM Desktop app








//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Invalid Parameter Handler
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+










 void __cdecl _invalid_parameter_noinfo(void);
 __declspec(noreturn) void __cdecl _invalid_parameter_noinfo_noreturn(void);

__declspec(noreturn)
 void __cdecl _invoke_watson(
      wchar_t const* _Expression,
      wchar_t const* _FunctionName,
      wchar_t const* _FileName,
            unsigned int _LineNo,
            uintptr_t _Reserved);


    



        // By default, _CRT_SECURE_INVALID_PARAMETER in retail invokes
        // _invalid_parameter_noinfo_noreturn(), which is marked
        // __declspec(noreturn) and does not return control to the application.
        // Even if _set_invalid_parameter_handler() is used to set a new invalid
        // parameter handler which does return control to the application,
        // _invalid_parameter_noinfo_noreturn() will terminate the application
        // and invoke Watson. You can overwrite the definition of
        // _CRT_SECURE_INVALID_PARAMETER if you need.
        //
        // _CRT_SECURE_INVALID_PARAMETER is used in the Standard C++ Libraries
        // and the SafeInt library.
        

    




//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Deprecation and Warnings
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+














    


        


    




//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Managed CRT Support
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

    






        
    



    


        
    








//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// SecureCRT Configuration
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+



























    







    





    


        


            
        
    













    


        



    



    
        
    





    
        // _CRT_SECURE_CPP_OVERLOAD_STANDARD_NAMES_COUNT is ignored if
        // _CRT_SECURE_CPP_OVERLOAD_STANDARD_NAMES is set to 0
        
    





    
        
              
        


    





    
        
    





    
        
    







    




//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Basic Types
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
typedef int                           errno_t;
typedef unsigned short                wint_t;
typedef unsigned short                wctype_t;
typedef long                          __time32_t;
typedef __int64                       __time64_t;

typedef struct __crt_locale_data_public
{
      unsigned short const* _locale_pctype;
      int _locale_mb_cur_max;
               unsigned int _locale_lc_codepage;
} __crt_locale_data_public;

typedef struct __crt_locale_pointers
{
    struct __crt_locale_data*    locinfo;
    struct __crt_multibyte_data* mbcinfo;
} __crt_locale_pointers;

typedef __crt_locale_pointers* _locale_t;

typedef struct _Mbstatet
{ // state of a multibyte translation
    unsigned long _Wchar;
    unsigned short _Byte, _State;
} _Mbstatet;

typedef _Mbstatet mbstate_t;










    


        typedef __time64_t time_t;
    


// Indicate that these common types are defined

    



    typedef size_t rsize_t;





//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// C++ Secure Overload Generation Macros
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

    























































































































































        
        
        
        
        
        
        
        
        
        
        
        

    







































































//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// C++ Standard Overload Generation Macros
//
//-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

    







































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































        
        
        
        

        

            


            


            


            


            


            


            


            


            



            



            


            


            


            


            


            


            


            


            


            


            



            



            



            


            



            




            

            




            

            




            

            




            

            




            

            




            

            




            

            




            

        












































    




__pragma(pack(pop))


__pragma(pack(push, 8))

#pragma warning(push)
#pragma warning(disable:4738) 
#pragma warning(disable:4820) 




    // Definition of the _exception struct, which is passed to the matherr function
    // when a floating point exception is detected:
    struct _exception
    {
        int    type;   // exception type - see below
        char*  name;   // name of function where error occurred
        double arg1;   // first argument to function
        double arg2;   // second argument (if any) to function
        double retval; // value to be returned by function
    };

    // Definition of the _complex struct to be used by those who use the complex
    // functions and want type checking.
    
        

        struct _complex
        {
            double x, y; // real and imaginary parts
        };

        
            // Non-ANSI name for compatibility
            
        
    




// On x86, when not using /arch:SSE2 or greater, floating point operations
// are performed using the x87 instruction set and FLT_EVAL_METHOD is 2.
// (When /fp:fast is used, floating point operations may be consistent, so
// we use the default types.)




    typedef float  float_t;
    typedef double double_t;




// Constant definitions for the exception type passed in the _exception struct







// Definitions of _HUGE and HUGE_VAL - respectively the XENIX and ANSI names
// for a value returned in case of error by a number of the floating point
// math routines.

    
        extern double const _HUGE;
    





    



























// Values for use as arguments to the _fperrraise function





























// IEEE 754 double properties





// IEEE 754 float properties





// IEEE 754 long double properties













void __cdecl _fperrraise(  int _Except);

   short __cdecl _dclass(  double _X);
   short __cdecl _ldclass(  long double _X);
   short __cdecl _fdclass(  float _X);

   int __cdecl _dsign(  double _X);
   int __cdecl _ldsign(  long double _X);
   int __cdecl _fdsign(  float _X);

   int __cdecl _dpcomp(  double _X,   double _Y);
   int __cdecl _ldpcomp(  long double _X,   long double _Y);
   int __cdecl _fdpcomp(  float _X,   float _Y);

   short __cdecl _dtest(  double* _Px);
   short __cdecl _ldtest(  long double* _Px);
   short __cdecl _fdtest(  float* _Px);

 short __cdecl _d_int(  double* _Px,   short _Xexp);
 short __cdecl _ld_int(  long double* _Px,   short _Xexp);
 short __cdecl _fd_int(  float* _Px,   short _Xexp);

 short __cdecl _dscale(  double* _Px,   long _Lexp);
 short __cdecl _ldscale(  long double* _Px,   long _Lexp);
 short __cdecl _fdscale(  float* _Px,   long _Lexp);

 short __cdecl _dunscale(  short* _Pex,   double* _Px);
 short __cdecl _ldunscale(  short* _Pex,   long double* _Px);
 short __cdecl _fdunscale(  short* _Pex,   float* _Px);

   short __cdecl _dexp(  double* _Px,   double _Y,   long _Eoff);
   short __cdecl _ldexp(  long double* _Px,   long double _Y,   long _Eoff);
   short __cdecl _fdexp(  float* _Px,   float _Y,   long _Eoff);

   short __cdecl _dnorm(  unsigned short* _Ps);
   short __cdecl _fdnorm(  unsigned short* _Ps);

   double __cdecl _dpoly(  double _X,   double const* _Tab,   int _N);
   long double __cdecl _ldpoly(  long double _X,   long double const* _Tab,   int _N);
   float __cdecl _fdpoly(  float _X,   float const* _Tab,   int _N);

   double __cdecl _dlog(  double _X,   int _Baseflag);
   long double __cdecl _ldlog(  long double _X,   int _Baseflag);
   float __cdecl _fdlog(  float _X,   int _Baseflag);

   double __cdecl _dsin(  double _X,   unsigned int _Qoff);
   long double __cdecl _ldsin(  long double _X,   unsigned int _Qoff);
   float __cdecl _fdsin(  float _X,   unsigned int _Qoff);

// double declarations
typedef union
{   // pun floating type as integer array
    unsigned short _Sh[4];
    double _Val;
} _double_val;

// float declarations
typedef union
{   // pun floating type as integer array
    unsigned short _Sh[2];
    float _Val;
} _float_val;

// long double declarations
typedef union
{   // pun floating type as integer array
    unsigned short _Sh[4];
    long double _Val;
} _ldouble_val;

typedef union
{   // pun float types as integer array
    unsigned short _Word[4];
    float _Float;
    double _Double;
    long double _Long_double;
} _float_const;

extern const _float_const _Denorm_C,  _Inf_C,  _Nan_C,  _Snan_C, _Hugeval_C;
extern const _float_const _FDenorm_C, _FInf_C, _FNan_C, _FSnan_C;
extern const _float_const _LDenorm_C, _LInf_C, _LNan_C, _LSnan_C;

extern const _float_const _Eps_C,  _Rteps_C;
extern const _float_const _FEps_C, _FRteps_C;
extern const _float_const _LEps_C, _LRteps_C;

extern const double      _Zero_C,  _Xbig_C;
extern const float       _FZero_C, _FXbig_C;
extern const long double _LZero_C, _LXbig_C;







    
    
    

    
    

    
    
    
    
    

    
    
    
    
    
    




















































































































































































      int       __cdecl abs(  int _X);
      long      __cdecl labs(  long _X);
      long long __cdecl llabs(  long long _X);

      double __cdecl acos(  double _X);
      double __cdecl asin(  double _X);
      double __cdecl atan(  double _X);
      double __cdecl atan2(  double _Y,   double _X);

      double __cdecl cos(  double _X);
      double __cdecl cosh(  double _X);
      double __cdecl exp(  double _X);
       double __cdecl fabs(  double _X);
      double __cdecl fmod(  double _X,   double _Y);
      double __cdecl log(  double _X);
      double __cdecl log10(  double _X);
      double __cdecl pow(  double _X,   double _Y);
      double __cdecl sin(  double _X);
      double __cdecl sinh(  double _X);
       double __cdecl sqrt(  double _X);
      double __cdecl tan(  double _X);
      double __cdecl tanh(  double _X);

       double    __cdecl acosh(  double _X);
       double    __cdecl asinh(  double _X);
       double    __cdecl atanh(  double _X);
        double    __cdecl atof(  char const* _String);
        double    __cdecl _atof_l(  char const* _String,   _locale_t _Locale);
       double    __cdecl _cabs(  struct _complex _Complex_value);
       double    __cdecl cbrt(  double _X);
       double    __cdecl ceil(  double _X);
       double    __cdecl _chgsign(  double _X);
       double    __cdecl copysign(  double _Number,   double _Sign);
       double    __cdecl _copysign(  double _Number,   double _Sign);
       double    __cdecl erf(  double _X);
       double    __cdecl erfc(  double _X);
       double    __cdecl exp2(  double _X);
       double    __cdecl expm1(  double _X);
       double    __cdecl fdim(  double _X,   double _Y);
       double    __cdecl floor(  double _X);
       double    __cdecl fma(  double _X,   double _Y,   double _Z);
       double    __cdecl fmax(  double _X,   double _Y);
       double    __cdecl fmin(  double _X,   double _Y);
       double    __cdecl frexp(  double _X,   int* _Y);
       double    __cdecl hypot(  double _X,   double _Y);
       double    __cdecl _hypot(  double _X,   double _Y);
       int       __cdecl ilogb(  double _X);
       double    __cdecl ldexp(  double _X,   int _Y);
       double    __cdecl lgamma(  double _X);
       long long __cdecl llrint(  double _X);
       long long __cdecl llround(  double _X);
       double    __cdecl log1p(  double _X);
       double    __cdecl log2(  double _X);
       double    __cdecl logb(  double _X);
       long      __cdecl lrint(  double _X);
       long      __cdecl lround(  double _X);

    int __cdecl _matherr(  struct _exception* _Except);

       double __cdecl modf(  double _X,   double* _Y);
       double __cdecl nan(  char const* _X);
       double __cdecl nearbyint(  double _X);
       double __cdecl nextafter(  double _X,   double _Y);
       double __cdecl nexttoward(  double _X,   long double _Y);
       double __cdecl remainder(  double _X,   double _Y);
       double __cdecl remquo(  double _X,   double _Y,   int* _Z);
       double __cdecl rint(  double _X);
       double __cdecl round(  double _X);
       double __cdecl scalbln(  double _X,   long _Y);
       double __cdecl scalbn(  double _X,   int _Y);
       double __cdecl tgamma(  double _X);
       double __cdecl trunc(  double _X);
       double __cdecl _j0(  double _X );
       double __cdecl _j1(  double _X );
       double __cdecl _jn(int _X,   double _Y);
       double __cdecl _y0(  double _X);
       double __cdecl _y1(  double _X);
       double __cdecl _yn(  int _X,   double _Y);

       float     __cdecl acoshf(  float _X);
       float     __cdecl asinhf(  float _X);
       float     __cdecl atanhf(  float _X);
       float     __cdecl cbrtf(  float _X);
       float     __cdecl _chgsignf(  float _X);
       float     __cdecl copysignf(  float _Number,   float _Sign);
       float     __cdecl _copysignf(  float _Number,   float _Sign);
       float     __cdecl erff(  float _X);
       float     __cdecl erfcf(  float _X);
       float     __cdecl expm1f(  float _X);
       float     __cdecl exp2f(  float _X);
       float     __cdecl fdimf(  float _X,   float _Y);
       float     __cdecl fmaf(  float _X,   float _Y,   float _Z);
       float     __cdecl fmaxf(  float _X,   float _Y);
       float     __cdecl fminf(  float _X,   float _Y);
       float     __cdecl _hypotf(  float _X,   float _Y);
       int       __cdecl ilogbf(  float _X);
       float     __cdecl lgammaf(  float _X);
       long long __cdecl llrintf(  float _X);
       long long __cdecl llroundf(  float _X);
       float     __cdecl log1pf(  float _X);
       float     __cdecl log2f(  float _X);
       float     __cdecl logbf(  float _X);
       long      __cdecl lrintf(  float _X);
       long      __cdecl lroundf(  float _X);
       float     __cdecl nanf(  char const* _X);
       float     __cdecl nearbyintf(  float _X);
       float     __cdecl nextafterf(  float _X,   float _Y);
       float     __cdecl nexttowardf(  float _X,   long double _Y);
       float     __cdecl remainderf(  float _X,   float _Y);
       float     __cdecl remquof(  float _X,   float _Y,   int* _Z);
       float     __cdecl rintf(  float _X);
       float     __cdecl roundf(  float _X);
       float     __cdecl scalblnf(  float _X,   long _Y);
       float     __cdecl scalbnf(  float _X,   int _Y);
       float     __cdecl tgammaf(  float _X);
       float     __cdecl truncf(  float _X);

    





    

           float __cdecl _logbf(  float _X);
           float __cdecl _nextafterf(  float _X,   float _Y);
           int   __cdecl _finitef(  float _X);
           int   __cdecl _isnanf(  float _X);
           int   __cdecl _fpclassf(  float _X);

           int   __cdecl _set_FMA3_enable(  int _Flag);
           int   __cdecl _get_FMA3_enable(void);

    








    

           float __cdecl acosf(  float _X);
           float __cdecl asinf(  float _X);
           float __cdecl atan2f(  float _Y,   float _X);
           float __cdecl atanf(  float _X);
           float __cdecl ceilf(  float _X);
           float __cdecl cosf(  float _X);
           float __cdecl coshf(  float _X);
           float __cdecl expf(  float _X);

    











































    





          __inline float __cdecl fabsf(  float _X)
        {
            return (float)fabs(_X);
        }

    

    

           float __cdecl floorf(  float _X);
           float __cdecl fmodf(  float _X,   float _Y);

    













      __inline float __cdecl frexpf(  float _X,   int *_Y)
    {
        return (float)frexp(_X, _Y);
    }

      __inline float __cdecl hypotf(  float _X,   float _Y)
    {
        return _hypotf(_X, _Y);
    }

      __inline float __cdecl ldexpf(  float _X,   int _Y)
    {
        return (float)ldexp(_X, _Y);
    }

    

           float  __cdecl log10f(  float _X);
           float  __cdecl logf(  float _X);
           float  __cdecl modff(  float _X,   float *_Y);
           float  __cdecl powf(  float _X,   float _Y);
           float  __cdecl sinf(  float _X);
           float  __cdecl sinhf(  float _X);
           float  __cdecl sqrtf(  float _X);
           float  __cdecl tanf(  float _X);
           float  __cdecl tanhf(  float _X);

    



















































       long double __cdecl acoshl(  long double _X);

      __inline long double __cdecl acosl(  long double _X)
    {
        return acos((double)_X);
    }

       long double __cdecl asinhl(  long double _X);

      __inline long double __cdecl asinl(  long double _X)
    {
        return asin((double)_X);
    }

      __inline long double __cdecl atan2l(  long double _Y,   long double _X)
    {
        return atan2((double)_Y, (double)_X);
    }

       long double __cdecl atanhl(  long double _X);

      __inline long double __cdecl atanl(  long double _X)
    {
        return atan((double)_X);
    }

       long double __cdecl cbrtl(  long double _X);

      __inline long double __cdecl ceill(  long double _X)
    {
        return ceil((double)_X);
    }

      __inline long double __cdecl _chgsignl(  long double _X)
    {
        return _chgsign((double)_X);
    }

       long double __cdecl copysignl(  long double _Number,   long double _Sign);

      __inline long double __cdecl _copysignl(  long double _Number,   long double _Sign)
    {
        return _copysign((double)_Number, (double)_Sign);
    }

      __inline long double __cdecl coshl(  long double _X)
    {
        return cosh((double)_X);
    }

      __inline long double __cdecl cosl(  long double _X)
    {
        return cos((double)_X);
    }

       long double __cdecl erfl(  long double _X);
       long double __cdecl erfcl(  long double _X);

      __inline long double __cdecl expl(  long double _X)
    {
        return exp((double)_X);
    }

       long double __cdecl exp2l(  long double _X);
       long double __cdecl expm1l(  long double _X);

      __inline long double __cdecl fabsl(  long double _X)
    {
        return fabs((double)_X);
    }

       long double __cdecl fdiml(  long double _X,   long double _Y);

      __inline long double __cdecl floorl(  long double _X)
    {
        return floor((double)_X);
    }

       long double __cdecl fmal(  long double _X,   long double _Y,   long double _Z);
       long double __cdecl fmaxl(  long double _X,   long double _Y);
       long double __cdecl fminl(  long double _X,   long double _Y);

      __inline long double __cdecl fmodl(  long double _X,   long double _Y)
    {
        return fmod((double)_X, (double)_Y);
    }

      __inline long double __cdecl frexpl(  long double _X,   int *_Y)
    {
        return frexp((double)_X, _Y);
    }

       int __cdecl ilogbl(  long double _X);

      __inline long double __cdecl _hypotl(  long double _X,   long double _Y)
    {
        return _hypot((double)_X, (double)_Y);
    }

      __inline long double __cdecl hypotl(  long double _X,   long double _Y)
    {
        return _hypot((double)_X, (double)_Y);
    }

      __inline long double __cdecl ldexpl(  long double _X,   int _Y)
    {
        return ldexp((double)_X, _Y);
    }

       long double __cdecl lgammal(  long double _X);
       long long __cdecl llrintl(  long double _X);
       long long __cdecl llroundl(  long double _X);

      __inline long double __cdecl logl(  long double _X)
    {
        return log((double)_X);
    }

      __inline long double __cdecl log10l(  long double _X)
    {
        return log10((double)_X);
    }

       long double __cdecl log1pl(  long double _X);
       long double __cdecl log2l(  long double _X);
       long double __cdecl logbl(  long double _X);
       long __cdecl lrintl(  long double _X);
       long __cdecl lroundl(  long double _X);

      __inline long double __cdecl modfl(  long double _X,   long double* _Y)
    {
        double _F, _I;
        _F = modf((double)_X, &_I);
        *_Y = _I;
        return _F;
    }

       long double __cdecl nanl(  char const* _X);
       long double __cdecl nearbyintl(  long double _X);
       long double __cdecl nextafterl(  long double _X,   long double _Y);
       long double __cdecl nexttowardl(  long double _X,   long double _Y);

      __inline long double __cdecl powl(  long double _X,   long double _Y)
    {
        return pow((double)_X, (double)_Y);
    }

       long double __cdecl remainderl(  long double _X,   long double _Y);
       long double __cdecl remquol(  long double _X,   long double _Y,   int* _Z);
       long double __cdecl rintl(  long double _X);
       long double __cdecl roundl(  long double _X);
       long double __cdecl scalblnl(  long double _X,   long _Y);
       long double __cdecl scalbnl(  long double _X,   int _Y);

      __inline long double __cdecl sinhl(  long double _X)
    {
        return sinh((double)_X);
    }

      __inline long double __cdecl sinl(  long double _X)
    {
        return sin((double)_X);
    }

      __inline long double __cdecl sqrtl(  long double _X)
    {
        return sqrt((double)_X);
    }

      __inline long double __cdecl tanhl(  long double _X)
    {
        return tanh((double)_X);
    }

      __inline long double __cdecl tanl(  long double _X)
    {
        return tan((double)_X);
    }

       long double __cdecl tgammal(  long double _X);
       long double __cdecl truncl(  long double _X);

    
        
    





    
    
    
    
    
    

    

    
        
            extern double HUGE;
        



        __declspec(deprecated("The POSIX name for this item is deprecated. Instead, use the ISO C " "and C++ conformant name: " "_j0" ". See online help for details."))    double __cdecl j0(  double _X);
        __declspec(deprecated("The POSIX name for this item is deprecated. Instead, use the ISO C " "and C++ conformant name: " "_j1" ". See online help for details."))    double __cdecl j1(  double _X);
        __declspec(deprecated("The POSIX name for this item is deprecated. Instead, use the ISO C " "and C++ conformant name: " "_jn" ". See online help for details."))    double __cdecl jn(  int _X,   double _Y);
        __declspec(deprecated("The POSIX name for this item is deprecated. Instead, use the ISO C " "and C++ conformant name: " "_y0" ". See online help for details."))    double __cdecl y0(  double _X);
        __declspec(deprecated("The POSIX name for this item is deprecated. Instead, use the ISO C " "and C++ conformant name: " "_y1" ". See online help for details."))    double __cdecl y1(  double _X);
        __declspec(deprecated("The POSIX name for this item is deprecated. Instead, use the ISO C " "and C++ conformant name: " "_yn" ". See online help for details."))    double __cdecl yn(  int _X,   double _Y);
    




#pragma warning(pop)

__pragma(pack(pop))








//----------------------------------------------------------------------------------
// Module Functions Definition - Utils math
//----------------------------------------------------------------------------------

// Clamp float value
inline float Clamp(float value, float min, float max)
{
    const float res = value < min ? min : value;
    return res > max ? max : res;
}

// Calculate linear interpolation between two floats
inline float Lerp(float start, float end, float amount)
{
    return start + amount*(end - start);
}

//----------------------------------------------------------------------------------
// Module Functions Definition - Vector2 math
//----------------------------------------------------------------------------------

// Vector with components value 0.0f
inline Vector2 Vector2Zero(void)
{
    Vector2 result = { 0.0f, 0.0f };
    return result;
}

// Vector with components value 1.0f
inline Vector2 Vector2One(void)
{
    Vector2 result = { 1.0f, 1.0f };
    return result;
}

// Add two vectors (v1 + v2)
inline Vector2 Vector2Add(Vector2 v1, Vector2 v2)
{
    Vector2 result = { v1.x + v2.x, v1.y + v2.y };
    return result;
}

// Subtract two vectors (v1 - v2)
inline Vector2 Vector2Subtract(Vector2 v1, Vector2 v2)
{
    Vector2 result = { v1.x - v2.x, v1.y - v2.y };
    return result;
}

// Calculate vector length
inline float Vector2Length(Vector2 v)
{
    float result = sqrtf((v.x*v.x) + (v.y*v.y));
    return result;
}

// Calculate two vectors dot product
inline float Vector2DotProduct(Vector2 v1, Vector2 v2)
{
    float result = (v1.x*v2.x + v1.y*v2.y);
    return result;
}

// Calculate distance between two vectors
inline float Vector2Distance(Vector2 v1, Vector2 v2)
{
    float result = sqrtf((v1.x - v2.x)*(v1.x - v2.x) + (v1.y - v2.y)*(v1.y - v2.y));
    return result;
}

// Calculate angle from two vectors in X-axis
inline float Vector2Angle(Vector2 v1, Vector2 v2)
{
    float result = atan2f(v2.y - v1.y, v2.x - v1.x)*(180.0f/3.14159265358979323846f);
    if (result < 0) result += 360.0f;
    return result;
}

// Scale vector (multiply by value)
inline Vector2 Vector2Scale(Vector2 v, float scale)
{
    Vector2 result = { v.x*scale, v.y*scale };
    return result;
}

// Multiply vector by vector
inline Vector2 Vector2MultiplyV(Vector2 v1, Vector2 v2)
{
    Vector2 result = { v1.x*v2.x, v1.y*v2.y };
    return result;
}

// Negate vector
inline Vector2 Vector2Negate(Vector2 v)
{
    Vector2 result = { -v.x, -v.y };
    return result;
}

// Divide vector by a float value
inline Vector2 Vector2Divide(Vector2 v, float div)
{
    Vector2 result = { v.x/div, v.y/div };
    return result;
}

// Divide vector by vector
inline Vector2 Vector2DivideV(Vector2 v1, Vector2 v2)
{
    Vector2 result = { v1.x/v2.x, v1.y/v2.y };
    return result;
}

// Normalize provided vector
inline Vector2 Vector2Normalize(Vector2 v)
{
    Vector2 result = Vector2Divide(v, Vector2Length(v));
    return result;
}

// Calculate linear interpolation between two vectors
inline Vector2 Vector2Lerp(Vector2 v1, Vector2 v2, float amount)
{
    Vector2 result = { 0 };

    result.x = v1.x + amount*(v2.x - v1.x);
    result.y = v1.y + amount*(v2.y - v1.y);

    return result;
}

//----------------------------------------------------------------------------------
// Module Functions Definition - Vector3 math
//----------------------------------------------------------------------------------

// Vector with components value 0.0f
inline Vector3 Vector3Zero(void)
{
    Vector3 result = { 0.0f, 0.0f, 0.0f };
    return result;
}

// Vector with components value 1.0f
inline Vector3 Vector3One(void)
{
    Vector3 result = { 1.0f, 1.0f, 1.0f };
    return result;
}

// Add two vectors
inline Vector3 Vector3Add(Vector3 v1, Vector3 v2)
{
    Vector3 result = { v1.x + v2.x, v1.y + v2.y, v1.z + v2.z };
    return result;
}

// Subtract two vectors
inline Vector3 Vector3Subtract(Vector3 v1, Vector3 v2)
{
    Vector3 result = { v1.x - v2.x, v1.y - v2.y, v1.z - v2.z };
    return result;
}

// Multiply vector by scalar
inline Vector3 Vector3Multiply(Vector3 v, float scalar)
{
    Vector3 result = { v.x*scalar, v.y*scalar, v.z*scalar };
    return result;
}

// Multiply vector by vector
inline Vector3 Vector3MultiplyV(Vector3 v1, Vector3 v2)
{
    Vector3 result = { v1.x*v2.x, v1.y*v2.y, v1.z*v2.z };
    return result;
}

// Calculate two vectors cross product
inline Vector3 Vector3CrossProduct(Vector3 v1, Vector3 v2)
{
    Vector3 result = { v1.y*v2.z - v1.z*v2.y, v1.z*v2.x - v1.x*v2.z, v1.x*v2.y - v1.y*v2.x };
    return result;
}

// Calculate one vector perpendicular vector
inline Vector3 Vector3Perpendicular(Vector3 v)
{
    Vector3 result = { 0 };

    float min = (float) fabs(v.x);
    Vector3 cardinalAxis = {1.0f, 0.0f, 0.0f};

    if (fabs(v.y) < min)
    {
        min = (float) fabs(v.y);
        Vector3 tmp = {0.0f, 1.0f, 0.0f};
        cardinalAxis = tmp;
    }

    if (fabs(v.z) < min)
    {
        Vector3 tmp = {0.0f, 0.0f, 1.0f};
        cardinalAxis = tmp;
    }

    result = Vector3CrossProduct(v, cardinalAxis);

    return result;
}

// Calculate vector length
inline float Vector3Length(const Vector3 v)
{
    float result = sqrtf(v.x*v.x + v.y*v.y + v.z*v.z);
    return result;
}

// Calculate two vectors dot product
inline float Vector3DotProduct(Vector3 v1, Vector3 v2)
{
    float result = (v1.x*v2.x + v1.y*v2.y + v1.z*v2.z);
    return result;
}

// Calculate distance between two vectors
inline float Vector3Distance(Vector3 v1, Vector3 v2)
{
    float dx = v2.x - v1.x;
    float dy = v2.y - v1.y;
    float dz = v2.z - v1.z;
    float result = sqrtf(dx*dx + dy*dy + dz*dz);
    return result;
}

// Scale provided vector
inline Vector3 Vector3Scale(Vector3 v, float scale)
{
    Vector3 result = { v.x*scale, v.y*scale, v.z*scale };
    return result;
}

// Negate provided vector (invert direction)
inline Vector3 Vector3Negate(Vector3 v)
{
    Vector3 result = { -v.x, -v.y, -v.z };
    return result;
}

// Divide vector by a float value
inline Vector3 Vector3Divide(Vector3 v, float div)
{
    Vector3 result = { v.x / div, v.y / div, v.z / div };
    return result;
}

// Divide vector by vector
inline Vector3 Vector3DivideV(Vector3 v1, Vector3 v2)
{
    Vector3 result = { v1.x/v2.x, v1.y/v2.y, v1.z/v2.z };
    return result;
}

// Normalize provided vector
inline Vector3 Vector3Normalize(Vector3 v)
{
    Vector3 result = v;

    float length, ilength;
    length = Vector3Length(v);
    if (length == 0.0f) length = 1.0f;
    ilength = 1.0f/length;

    result.x *= ilength;
    result.y *= ilength;
    result.z *= ilength;

    return result;
}

// Orthonormalize provided vectors
// Makes vectors normalized and orthogonal to each other
// Gram-Schmidt function implementation
inline void Vector3OrthoNormalize(Vector3 *v1, Vector3 *v2)
{
    *v1 = Vector3Normalize(*v1);
    Vector3 vn = Vector3CrossProduct(*v1, *v2);
    vn = Vector3Normalize(vn);
    *v2 = Vector3CrossProduct(vn, *v1);
}

// Transforms a Vector3 by a given Matrix
inline Vector3 Vector3Transform(Vector3 v, Matrix mat)
{
    Vector3 result = { 0 };
    float x = v.x;
    float y = v.y;
    float z = v.z;

    result.x = mat.m0*x + mat.m4*y + mat.m8*z + mat.m12;
    result.y = mat.m1*x + mat.m5*y + mat.m9*z + mat.m13;
    result.z = mat.m2*x + mat.m6*y + mat.m10*z + mat.m14;

    return result;
}

// Transform a vector by quaternion rotation
inline Vector3 Vector3RotateByQuaternion(Vector3 v, Quaternion q)
{
    Vector3 result = { 0 };

    result.x = v.x*(q.x*q.x + q.w*q.w - q.y*q.y - q.z*q.z) + v.y*(2*q.x*q.y - 2*q.w*q.z) + v.z*(2*q.x*q.z + 2*q.w*q.y);
    result.y = v.x*(2*q.w*q.z + 2*q.x*q.y) + v.y*(q.w*q.w - q.x*q.x + q.y*q.y - q.z*q.z) + v.z*(-2*q.w*q.x + 2*q.y*q.z);
    result.z = v.x*(-2*q.w*q.y + 2*q.x*q.z) + v.y*(2*q.w*q.x + 2*q.y*q.z)+ v.z*(q.w*q.w - q.x*q.x - q.y*q.y + q.z*q.z);

    return result;
}

// Calculate linear interpolation between two vectors
inline Vector3 Vector3Lerp(Vector3 v1, Vector3 v2, float amount)
{
    Vector3 result = { 0 };

    result.x = v1.x + amount*(v2.x - v1.x);
    result.y = v1.y + amount*(v2.y - v1.y);
    result.z = v1.z + amount*(v2.z - v1.z);

    return result;
}

// Calculate reflected vector to normal
inline Vector3 Vector3Reflect(Vector3 v, Vector3 normal)
{
    // I is the original vector
    // N is the normal of the incident plane
    // R = I - (2*N*( DotProduct[ I,N] ))

    Vector3 result = { 0 };

    float dotProduct = Vector3DotProduct(v, normal);

    result.x = v.x - (2.0f*normal.x)*dotProduct;
    result.y = v.y - (2.0f*normal.y)*dotProduct;
    result.z = v.z - (2.0f*normal.z)*dotProduct;

    return result;
}

// Return min value for each pair of components
inline Vector3 Vector3Min(Vector3 v1, Vector3 v2)
{
    Vector3 result = { 0 };

    result.x = fminf(v1.x, v2.x);
    result.y = fminf(v1.y, v2.y);
    result.z = fminf(v1.z, v2.z);

    return result;
}

// Return max value for each pair of components
inline Vector3 Vector3Max(Vector3 v1, Vector3 v2)
{
    Vector3 result = { 0 };

    result.x = fmaxf(v1.x, v2.x);
    result.y = fmaxf(v1.y, v2.y);
    result.z = fmaxf(v1.z, v2.z);

    return result;
}

// Compute barycenter coordinates (u, v, w) for point p with respect to triangle (a, b, c)
// NOTE: Assumes P is on the plane of the triangle
inline Vector3 Vector3Barycenter(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
{
    //Vector v0 = b - a, v1 = c - a, v2 = p - a;

    Vector3 v0 = Vector3Subtract(b, a);
    Vector3 v1 = Vector3Subtract(c, a);
    Vector3 v2 = Vector3Subtract(p, a);
    float d00 = Vector3DotProduct(v0, v0);
    float d01 = Vector3DotProduct(v0, v1);
    float d11 = Vector3DotProduct(v1, v1);
    float d20 = Vector3DotProduct(v2, v0);
    float d21 = Vector3DotProduct(v2, v1);

    float denom = d00*d11 - d01*d01;

    Vector3 result = { 0 };

    result.y = (d11*d20 - d01*d21)/denom;
    result.z = (d00*d21 - d01*d20)/denom;
    result.x = 1.0f - (result.z + result.y);

    return result;
}

// Returns Vector3 as float array
inline float3 Vector3ToFloatV(Vector3 v)
{
    float3 buffer = { 0 };

    buffer.v[0] = v.x;
    buffer.v[1] = v.y;
    buffer.v[2] = v.z;

    return buffer;
}

//----------------------------------------------------------------------------------
// Module Functions Definition - Matrix math
//----------------------------------------------------------------------------------

// Compute matrix determinant
inline float MatrixDeterminant(Matrix mat)
{
    float result = { 0 };

    // Cache the matrix values (speed optimization)
    float a00 = mat.m0, a01 = mat.m1, a02 = mat.m2, a03 = mat.m3;
    float a10 = mat.m4, a11 = mat.m5, a12 = mat.m6, a13 = mat.m7;
    float a20 = mat.m8, a21 = mat.m9, a22 = mat.m10, a23 = mat.m11;
    float a30 = mat.m12, a31 = mat.m13, a32 = mat.m14, a33 = mat.m15;

    result = a30*a21*a12*a03 - a20*a31*a12*a03 - a30*a11*a22*a03 + a10*a31*a22*a03 +
             a20*a11*a32*a03 - a10*a21*a32*a03 - a30*a21*a02*a13 + a20*a31*a02*a13 +
             a30*a01*a22*a13 - a00*a31*a22*a13 - a20*a01*a32*a13 + a00*a21*a32*a13 +
             a30*a11*a02*a23 - a10*a31*a02*a23 - a30*a01*a12*a23 + a00*a31*a12*a23 +
             a10*a01*a32*a23 - a00*a11*a32*a23 - a20*a11*a02*a33 + a10*a21*a02*a33 +
             a20*a01*a12*a33 - a00*a21*a12*a33 - a10*a01*a22*a33 + a00*a11*a22*a33;

    return result;
}

// Returns the trace of the matrix (sum of the values along the diagonal)
inline float MatrixTrace(Matrix mat)
{
    float result = (mat.m0 + mat.m5 + mat.m10 + mat.m15);
    return result;
}

// Transposes provided matrix
inline Matrix MatrixTranspose(Matrix mat)
{
    Matrix result = { 0 };

    result.m0 = mat.m0;
    result.m1 = mat.m4;
    result.m2 = mat.m8;
    result.m3 = mat.m12;
    result.m4 = mat.m1;
    result.m5 = mat.m5;
    result.m6 = mat.m9;
    result.m7 = mat.m13;
    result.m8 = mat.m2;
    result.m9 = mat.m6;
    result.m10 = mat.m10;
    result.m11 = mat.m14;
    result.m12 = mat.m3;
    result.m13 = mat.m7;
    result.m14 = mat.m11;
    result.m15 = mat.m15;

    return result;
}

// Invert provided matrix
inline Matrix MatrixInvert(Matrix mat)
{
    Matrix result = { 0 };

    // Cache the matrix values (speed optimization)
    float a00 = mat.m0, a01 = mat.m1, a02 = mat.m2, a03 = mat.m3;
    float a10 = mat.m4, a11 = mat.m5, a12 = mat.m6, a13 = mat.m7;
    float a20 = mat.m8, a21 = mat.m9, a22 = mat.m10, a23 = mat.m11;
    float a30 = mat.m12, a31 = mat.m13, a32 = mat.m14, a33 = mat.m15;

    float b00 = a00*a11 - a01*a10;
    float b01 = a00*a12 - a02*a10;
    float b02 = a00*a13 - a03*a10;
    float b03 = a01*a12 - a02*a11;
    float b04 = a01*a13 - a03*a11;
    float b05 = a02*a13 - a03*a12;
    float b06 = a20*a31 - a21*a30;
    float b07 = a20*a32 - a22*a30;
    float b08 = a20*a33 - a23*a30;
    float b09 = a21*a32 - a22*a31;
    float b10 = a21*a33 - a23*a31;
    float b11 = a22*a33 - a23*a32;

    // Calculate the invert determinant (inlined to avoid double-caching)
    float invDet = 1.0f/(b00*b11 - b01*b10 + b02*b09 + b03*b08 - b04*b07 + b05*b06);

    result.m0 = (a11*b11 - a12*b10 + a13*b09)*invDet;
    result.m1 = (-a01*b11 + a02*b10 - a03*b09)*invDet;
    result.m2 = (a31*b05 - a32*b04 + a33*b03)*invDet;
    result.m3 = (-a21*b05 + a22*b04 - a23*b03)*invDet;
    result.m4 = (-a10*b11 + a12*b08 - a13*b07)*invDet;
    result.m5 = (a00*b11 - a02*b08 + a03*b07)*invDet;
    result.m6 = (-a30*b05 + a32*b02 - a33*b01)*invDet;
    result.m7 = (a20*b05 - a22*b02 + a23*b01)*invDet;
    result.m8 = (a10*b10 - a11*b08 + a13*b06)*invDet;
    result.m9 = (-a00*b10 + a01*b08 - a03*b06)*invDet;
    result.m10 = (a30*b04 - a31*b02 + a33*b00)*invDet;
    result.m11 = (-a20*b04 + a21*b02 - a23*b00)*invDet;
    result.m12 = (-a10*b09 + a11*b07 - a12*b06)*invDet;
    result.m13 = (a00*b09 - a01*b07 + a02*b06)*invDet;
    result.m14 = (-a30*b03 + a31*b01 - a32*b00)*invDet;
    result.m15 = (a20*b03 - a21*b01 + a22*b00)*invDet;

    return result;
}

// Normalize provided matrix
inline Matrix MatrixNormalize(Matrix mat)
{
    Matrix result = { 0 };

    float det = MatrixDeterminant(mat);

    result.m0 = mat.m0/det;
    result.m1 = mat.m1/det;
    result.m2 = mat.m2/det;
    result.m3 = mat.m3/det;
    result.m4 = mat.m4/det;
    result.m5 = mat.m5/det;
    result.m6 = mat.m6/det;
    result.m7 = mat.m7/det;
    result.m8 = mat.m8/det;
    result.m9 = mat.m9/det;
    result.m10 = mat.m10/det;
    result.m11 = mat.m11/det;
    result.m12 = mat.m12/det;
    result.m13 = mat.m13/det;
    result.m14 = mat.m14/det;
    result.m15 = mat.m15/det;

    return result;
}

// Returns identity matrix
inline Matrix MatrixIdentity(void)
{
    Matrix result = { 1.0f, 0.0f, 0.0f, 0.0f,
                      0.0f, 1.0f, 0.0f, 0.0f,
                      0.0f, 0.0f, 1.0f, 0.0f,
                      0.0f, 0.0f, 0.0f, 1.0f };

    return result;
}

// Add two matrices
inline Matrix MatrixAdd(Matrix left, Matrix right)
{
    Matrix result = MatrixIdentity();

    result.m0 = left.m0 + right.m0;
    result.m1 = left.m1 + right.m1;
    result.m2 = left.m2 + right.m2;
    result.m3 = left.m3 + right.m3;
    result.m4 = left.m4 + right.m4;
    result.m5 = left.m5 + right.m5;
    result.m6 = left.m6 + right.m6;
    result.m7 = left.m7 + right.m7;
    result.m8 = left.m8 + right.m8;
    result.m9 = left.m9 + right.m9;
    result.m10 = left.m10 + right.m10;
    result.m11 = left.m11 + right.m11;
    result.m12 = left.m12 + right.m12;
    result.m13 = left.m13 + right.m13;
    result.m14 = left.m14 + right.m14;
    result.m15 = left.m15 + right.m15;

    return result;
}

// Subtract two matrices (left - right)
inline Matrix MatrixSubtract(Matrix left, Matrix right)
{
    Matrix result = MatrixIdentity();

    result.m0 = left.m0 - right.m0;
    result.m1 = left.m1 - right.m1;
    result.m2 = left.m2 - right.m2;
    result.m3 = left.m3 - right.m3;
    result.m4 = left.m4 - right.m4;
    result.m5 = left.m5 - right.m5;
    result.m6 = left.m6 - right.m6;
    result.m7 = left.m7 - right.m7;
    result.m8 = left.m8 - right.m8;
    result.m9 = left.m9 - right.m9;
    result.m10 = left.m10 - right.m10;
    result.m11 = left.m11 - right.m11;
    result.m12 = left.m12 - right.m12;
    result.m13 = left.m13 - right.m13;
    result.m14 = left.m14 - right.m14;
    result.m15 = left.m15 - right.m15;

    return result;
}

// Returns translation matrix
inline Matrix MatrixTranslate(float x, float y, float z)
{
    Matrix result = { 1.0f, 0.0f, 0.0f, x,
                      0.0f, 1.0f, 0.0f, y,
                      0.0f, 0.0f, 1.0f, z,
                      0.0f, 0.0f, 0.0f, 1.0f };

    return result;
}

// Create rotation matrix from axis and angle
// NOTE: Angle should be provided in radians
inline Matrix MatrixRotate(Vector3 axis, float angle)
{
    Matrix result = { 0 };

    float x = axis.x, y = axis.y, z = axis.z;

    float length = sqrtf(x*x + y*y + z*z);

    if ((length != 1.0f) && (length != 0.0f))
    {
        length = 1.0f/length;
        x *= length;
        y *= length;
        z *= length;
    }

    float sinres = sinf(angle);
    float cosres = cosf(angle);
    float t = 1.0f - cosres;

    result.m0  = x*x*t + cosres;
    result.m1  = y*x*t + z*sinres;
    result.m2  = z*x*t - y*sinres;
    result.m3  = 0.0f;

    result.m4  = x*y*t - z*sinres;
    result.m5  = y*y*t + cosres;
    result.m6  = z*y*t + x*sinres;
    result.m7  = 0.0f;

    result.m8  = x*z*t + y*sinres;
    result.m9  = y*z*t - x*sinres;
    result.m10 = z*z*t + cosres;
    result.m11 = 0.0f;

    result.m12 = 0.0f;
    result.m13 = 0.0f;
    result.m14 = 0.0f;
    result.m15 = 1.0f;

    return result;
}

// Returns x-rotation matrix (angle in radians)
inline Matrix MatrixRotateX(float angle)
{
    Matrix result = MatrixIdentity();

    float cosres = cosf(angle);
    float sinres = sinf(angle);

    result.m5 = cosres;
    result.m6 = -sinres;
    result.m9 = sinres;
    result.m10 = cosres;

    return result;
}

// Returns y-rotation matrix (angle in radians)
inline Matrix MatrixRotateY(float angle)
{
    Matrix result = MatrixIdentity();

    float cosres = cosf(angle);
    float sinres = sinf(angle);

    result.m0 = cosres;
    result.m2 = sinres;
    result.m8 = -sinres;
    result.m10 = cosres;

    return result;
}

// Returns z-rotation matrix (angle in radians)
inline Matrix MatrixRotateZ(float angle)
{
    Matrix result = MatrixIdentity();

    float cosres = cosf(angle);
    float sinres = sinf(angle);

    result.m0 = cosres;
    result.m1 = -sinres;
    result.m4 = sinres;
    result.m5 = cosres;

    return result;
}

// Returns scaling matrix
inline Matrix MatrixScale(float x, float y, float z)
{
    Matrix result = { x, 0.0f, 0.0f, 0.0f,
                      0.0f, y, 0.0f, 0.0f,
                      0.0f, 0.0f, z, 0.0f,
                      0.0f, 0.0f, 0.0f, 1.0f };

    return result;
}

// Returns two matrix multiplication
// NOTE: When multiplying matrices... the order matters!
inline Matrix MatrixMultiply(Matrix left, Matrix right)
{
    Matrix result = { 0 };

    result.m0 = left.m0*right.m0 + left.m1*right.m4 + left.m2*right.m8 + left.m3*right.m12;
    result.m1 = left.m0*right.m1 + left.m1*right.m5 + left.m2*right.m9 + left.m3*right.m13;
    result.m2 = left.m0*right.m2 + left.m1*right.m6 + left.m2*right.m10 + left.m3*right.m14;
    result.m3 = left.m0*right.m3 + left.m1*right.m7 + left.m2*right.m11 + left.m3*right.m15;
    result.m4 = left.m4*right.m0 + left.m5*right.m4 + left.m6*right.m8 + left.m7*right.m12;
    result.m5 = left.m4*right.m1 + left.m5*right.m5 + left.m6*right.m9 + left.m7*right.m13;
    result.m6 = left.m4*right.m2 + left.m5*right.m6 + left.m6*right.m10 + left.m7*right.m14;
    result.m7 = left.m4*right.m3 + left.m5*right.m7 + left.m6*right.m11 + left.m7*right.m15;
    result.m8 = left.m8*right.m0 + left.m9*right.m4 + left.m10*right.m8 + left.m11*right.m12;
    result.m9 = left.m8*right.m1 + left.m9*right.m5 + left.m10*right.m9 + left.m11*right.m13;
    result.m10 = left.m8*right.m2 + left.m9*right.m6 + left.m10*right.m10 + left.m11*right.m14;
    result.m11 = left.m8*right.m3 + left.m9*right.m7 + left.m10*right.m11 + left.m11*right.m15;
    result.m12 = left.m12*right.m0 + left.m13*right.m4 + left.m14*right.m8 + left.m15*right.m12;
    result.m13 = left.m12*right.m1 + left.m13*right.m5 + left.m14*right.m9 + left.m15*right.m13;
    result.m14 = left.m12*right.m2 + left.m13*right.m6 + left.m14*right.m10 + left.m15*right.m14;
    result.m15 = left.m12*right.m3 + left.m13*right.m7 + left.m14*right.m11 + left.m15*right.m15;

    return result;
}

// Returns perspective projection matrix
inline Matrix MatrixFrustum(double left, double right, double bottom, double top, double near, double far)
{
    Matrix result = { 0 };

    float rl = (float)(right - left);
    float tb = (float)(top - bottom);
    float fn = (float)(far - near);

    result.m0 = ((float) near*2.0f)/rl;
    result.m1 = 0.0f;
    result.m2 = 0.0f;
    result.m3 = 0.0f;

    result.m4 = 0.0f;
    result.m5 = ((float) near*2.0f)/tb;
    result.m6 = 0.0f;
    result.m7 = 0.0f;

    result.m8 = ((float)right + (float)left)/rl;
    result.m9 = ((float)top + (float)bottom)/tb;
    result.m10 = -((float)far + (float)near)/fn;
    result.m11 = -1.0f;

    result.m12 = 0.0f;
    result.m13 = 0.0f;
    result.m14 = -((float)far*(float)near*2.0f)/fn;
    result.m15 = 0.0f;

    return result;
}

// Returns perspective projection matrix
// NOTE: Angle should be provided in radians
inline Matrix MatrixPerspective(double fovy, double aspect, double near, double far)
{
    double top = near*tan(fovy*0.5);
    double right = top*aspect;
    Matrix result = MatrixFrustum(-right, right, -top, top, near, far);

    return result;
}

// Returns orthographic projection matrix
inline Matrix MatrixOrtho(double left, double right, double bottom, double top, double near, double far)
{
    Matrix result = { 0 };

    float rl = (float)(right - left);
    float tb = (float)(top - bottom);
    float fn = (float)(far - near);

    result.m0 = 2.0f/rl;
    result.m1 = 0.0f;
    result.m2 = 0.0f;
    result.m3 = 0.0f;
    result.m4 = 0.0f;
    result.m5 = 2.0f/tb;
    result.m6 = 0.0f;
    result.m7 = 0.0f;
    result.m8 = 0.0f;
    result.m9 = 0.0f;
    result.m10 = -2.0f/fn;
    result.m11 = 0.0f;
    result.m12 = -((float)left + (float)right)/rl;
    result.m13 = -((float)top + (float)bottom)/tb;
    result.m14 = -((float)far + (float)near)/fn;
    result.m15 = 1.0f;

    return result;
}

// Returns camera look-at matrix (view matrix)
inline Matrix MatrixLookAt(Vector3 eye, Vector3 target, Vector3 up)
{
    Matrix result = { 0 };

    Vector3 z = Vector3Subtract(eye, target);
    z = Vector3Normalize(z);
    Vector3 x = Vector3CrossProduct(up, z);
    x = Vector3Normalize(x);
    Vector3 y = Vector3CrossProduct(z, x);
    y = Vector3Normalize(y);

    result.m0 = x.x;
    result.m1 = x.y;
    result.m2 = x.z;
    result.m3 = 0.0f;
    result.m4 = y.x;
    result.m5 = y.y;
    result.m6 = y.z;
    result.m7 = 0.0f;
    result.m8 = z.x;
    result.m9 = z.y;
    result.m10 = z.z;
    result.m11 = 0.0f;
    result.m12 = eye.x;
    result.m13 = eye.y;
    result.m14 = eye.z;
    result.m15 = 1.0f;

    result = MatrixInvert(result);

    return result;
}

// Returns float array of matrix data
inline float16 MatrixToFloatV(Matrix mat)
{
    float16 buffer = { 0 };

    buffer.v[0] = mat.m0;
    buffer.v[1] = mat.m1;
    buffer.v[2] = mat.m2;
    buffer.v[3] = mat.m3;
    buffer.v[4] = mat.m4;
    buffer.v[5] = mat.m5;
    buffer.v[6] = mat.m6;
    buffer.v[7] = mat.m7;
    buffer.v[8] = mat.m8;
    buffer.v[9] = mat.m9;
    buffer.v[10] = mat.m10;
    buffer.v[11] = mat.m11;
    buffer.v[12] = mat.m12;
    buffer.v[13] = mat.m13;
    buffer.v[14] = mat.m14;
    buffer.v[15] = mat.m15;

    return buffer;
}

//----------------------------------------------------------------------------------
// Module Functions Definition - Quaternion math
//----------------------------------------------------------------------------------

// Returns identity quaternion
inline Quaternion QuaternionIdentity(void)
{
    Quaternion result = { 0.0f, 0.0f, 0.0f, 1.0f };
    return result;
}

// Computes the length of a quaternion
inline float QuaternionLength(Quaternion q)
{
    float result = (float)sqrt(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w);
    return result;
}

// Normalize provided quaternion
inline Quaternion QuaternionNormalize(Quaternion q)
{
    Quaternion result = { 0 };

    float length, ilength;
    length = QuaternionLength(q);
    if (length == 0.0f) length = 1.0f;
    ilength = 1.0f/length;

    result.x = q.x*ilength;
    result.y = q.y*ilength;
    result.z = q.z*ilength;
    result.w = q.w*ilength;

    return result;
}

// Invert provided quaternion
inline Quaternion QuaternionInvert(Quaternion q)
{
    Quaternion result = q;
    float length = QuaternionLength(q);
    float lengthSq = length*length;

    if (lengthSq != 0.0)
    {
        float i = 1.0f/lengthSq;

        result.x *= -i;
        result.y *= -i;
        result.z *= -i;
        result.w *= i;
    }

    return result;
}

// Calculate two quaternion multiplication
inline Quaternion QuaternionMultiply(Quaternion q1, Quaternion q2)
{
    Quaternion result = { 0 };

    float qax = q1.x, qay = q1.y, qaz = q1.z, qaw = q1.w;
    float qbx = q2.x, qby = q2.y, qbz = q2.z, qbw = q2.w;

    result.x = qax*qbw + qaw*qbx + qay*qbz - qaz*qby;
    result.y = qay*qbw + qaw*qby + qaz*qbx - qax*qbz;
    result.z = qaz*qbw + qaw*qbz + qax*qby - qay*qbx;
    result.w = qaw*qbw - qax*qbx - qay*qby - qaz*qbz;

    return result;
}

// Calculate linear interpolation between two quaternions
inline Quaternion QuaternionLerp(Quaternion q1, Quaternion q2, float amount)
{
    Quaternion result = { 0 };

    result.x = q1.x + amount*(q2.x - q1.x);
    result.y = q1.y + amount*(q2.y - q1.y);
    result.z = q1.z + amount*(q2.z - q1.z);
    result.w = q1.w + amount*(q2.w - q1.w);

    return result;
}

// Calculate slerp-optimized interpolation between two quaternions
inline Quaternion QuaternionNlerp(Quaternion q1, Quaternion q2, float amount)
{
    Quaternion result = QuaternionLerp(q1, q2, amount);
    result = QuaternionNormalize(result);

    return result;
}

// Calculates spherical linear interpolation between two quaternions
inline Quaternion QuaternionSlerp(Quaternion q1, Quaternion q2, float amount)
{
    Quaternion result = { 0 };

    float cosHalfTheta =  q1.x*q2.x + q1.y*q2.y + q1.z*q2.z + q1.w*q2.w;

    if (fabs(cosHalfTheta) >= 1.0f) result = q1;
    else if (cosHalfTheta > 0.95f) result = QuaternionNlerp(q1, q2, amount);
    else
    {
        float halfTheta = (float) acos(cosHalfTheta);
        float sinHalfTheta = (float) sqrt(1.0f - cosHalfTheta*cosHalfTheta);

        if (fabs(sinHalfTheta) < 0.001f)
        {
            result.x = (q1.x*0.5f + q2.x*0.5f);
            result.y = (q1.y*0.5f + q2.y*0.5f);
            result.z = (q1.z*0.5f + q2.z*0.5f);
            result.w = (q1.w*0.5f + q2.w*0.5f);
        }
        else
        {
            float ratioA = sinf((1 - amount)*halfTheta)/sinHalfTheta;
            float ratioB = sinf(amount*halfTheta)/sinHalfTheta;

            result.x = (q1.x*ratioA + q2.x*ratioB);
            result.y = (q1.y*ratioA + q2.y*ratioB);
            result.z = (q1.z*ratioA + q2.z*ratioB);
            result.w = (q1.w*ratioA + q2.w*ratioB);
        }
    }

    return result;
}

// Calculate quaternion based on the rotation from one vector to another
inline Quaternion QuaternionFromVector3ToVector3(Vector3 from, Vector3 to)
{
    Quaternion result = { 0 };

    float cos2Theta = Vector3DotProduct(from, to);
    Vector3 cross = Vector3CrossProduct(from, to);

    result.x = cross.x;
    result.y = cross.y;
    result.z = cross.y;
    result.w = 1.0f + cos2Theta;     // NOTE: Added QuaternioIdentity()

    // Normalize to essentially nlerp the original and identity to 0.5
    result = QuaternionNormalize(result);

    // Above lines are equivalent to:
    //Quaternion result = QuaternionNlerp(q, QuaternionIdentity(), 0.5f);

    return result;
}

// Returns a quaternion for a given rotation matrix
inline Quaternion QuaternionFromMatrix(Matrix mat)
{
    Quaternion result = { 0 };

    float trace = MatrixTrace(mat);

    if (trace > 0.0f)
    {
        float s = (float)sqrt(trace + 1)*2.0f;
        float invS = 1.0f/s;

        result.w = s*0.25f;
        result.x = (mat.m6 - mat.m9)*invS;
        result.y = (mat.m8 - mat.m2)*invS;
        result.z = (mat.m1 - mat.m4)*invS;
    }
    else
    {
        float m00 = mat.m0, m11 = mat.m5, m22 = mat.m10;

        if (m00 > m11 && m00 > m22)
        {
            float s = (float)sqrt(1.0f + m00 - m11 - m22)*2.0f;
            float invS = 1.0f/s;

            result.w = (mat.m6 - mat.m9)*invS;
            result.x = s*0.25f;
            result.y = (mat.m4 + mat.m1)*invS;
            result.z = (mat.m8 + mat.m2)*invS;
        }
        else if (m11 > m22)
        {
            float s = (float)sqrt(1.0f + m11 - m00 - m22)*2.0f;
            float invS = 1.0f/s;

            result.w = (mat.m8 - mat.m2)*invS;
            result.x = (mat.m4 + mat.m1)*invS;
            result.y = s*0.25f;
            result.z = (mat.m9 + mat.m6)*invS;
        }
        else
        {
            float s = (float)sqrt(1.0f + m22 - m00 - m11)*2.0f;
            float invS = 1.0f/s;

            result.w = (mat.m1 - mat.m4)*invS;
            result.x = (mat.m8 + mat.m2)*invS;
            result.y = (mat.m9 + mat.m6)*invS;
            result.z = s*0.25f;
        }
    }

    return result;
}

// Returns a matrix for a given quaternion
inline Matrix QuaternionToMatrix(Quaternion q)
{
    Matrix result = { 0 };

    float x = q.x, y = q.y, z = q.z, w = q.w;

    float x2 = x + x;
    float y2 = y + y;
    float z2 = z + z;

    float length = QuaternionLength(q);
    float lengthSquared = length*length;

    float xx = x*x2/lengthSquared;
    float xy = x*y2/lengthSquared;
    float xz = x*z2/lengthSquared;

    float yy = y*y2/lengthSquared;
    float yz = y*z2/lengthSquared;
    float zz = z*z2/lengthSquared;

    float wx = w*x2/lengthSquared;
    float wy = w*y2/lengthSquared;
    float wz = w*z2/lengthSquared;

    result.m0 = 1.0f - (yy + zz);
    result.m1 = xy - wz;
    result.m2 = xz + wy;
    result.m3 = 0.0f;
    result.m4 = xy + wz;
    result.m5 = 1.0f - (xx + zz);
    result.m6 = yz - wx;
    result.m7 = 0.0f;
    result.m8 = xz - wy;
    result.m9 = yz + wx;
    result.m10 = 1.0f - (xx + yy);
    result.m11 = 0.0f;
    result.m12 = 0.0f;
    result.m13 = 0.0f;
    result.m14 = 0.0f;
    result.m15 = 1.0f;

    return result;
}

// Returns rotation quaternion for an angle and axis
// NOTE: angle must be provided in radians
inline Quaternion QuaternionFromAxisAngle(Vector3 axis, float angle)
{
    Quaternion result = { 0.0f, 0.0f, 0.0f, 1.0f };

    if (Vector3Length(axis) != 0.0f)

    angle *= 0.5f;

    axis = Vector3Normalize(axis);

    float sinres = sinf(angle);
    float cosres = cosf(angle);

    result.x = axis.x*sinres;
    result.y = axis.y*sinres;
    result.z = axis.z*sinres;
    result.w = cosres;

    result = QuaternionNormalize(result);

    return result;
}

// Returns the rotation angle and axis for a given quaternion
inline void QuaternionToAxisAngle(Quaternion q, Vector3 *outAxis, float *outAngle)
{
    if (fabs(q.w) > 1.0f) q = QuaternionNormalize(q);

    Vector3 resAxis = { 0.0f, 0.0f, 0.0f };
    float resAngle = 0.0f;

    resAngle = 2.0f*(float)acos(q.w);
    float den = (float)sqrt(1.0f - q.w*q.w);

    if (den > 0.0001f)
    {
        resAxis.x = q.x/den;
        resAxis.y = q.y/den;
        resAxis.z = q.z/den;
    }
    else
    {
        // This occurs when the angle is zero.
        // Not a problem: just set an arbitrary normalized axis.
        resAxis.x = 1.0f;
    }

    *outAxis = resAxis;
    *outAngle = resAngle;
}

// Returns he quaternion equivalent to Euler angles
inline Quaternion QuaternionFromEuler(float roll, float pitch, float yaw)
{
    Quaternion q = { 0 };

    float x0 = cosf(roll*0.5f);
    float x1 = sinf(roll*0.5f);
    float y0 = cosf(pitch*0.5f);
    float y1 = sinf(pitch*0.5f);
    float z0 = cosf(yaw*0.5f);
    float z1 = sinf(yaw*0.5f);

    q.x = x1*y0*z0 - x0*y1*z1;
    q.y = x0*y1*z0 + x1*y0*z1;
    q.z = x0*y0*z1 - x1*y1*z0;
    q.w = x0*y0*z0 + x1*y1*z1;

    return q;
}

// Return the Euler angles equivalent to quaternion (roll, pitch, yaw)
// NOTE: Angles are returned in a Vector3 struct in degrees
inline Vector3 QuaternionToEuler(Quaternion q)
{
    Vector3 result = { 0 };

    // roll (x-axis rotation)
    float x0 = 2.0f*(q.w*q.x + q.y*q.z);
    float x1 = 1.0f - 2.0f*(q.x*q.x + q.y*q.y);
    result.x = atan2f(x0, x1)*(180.0f/3.14159265358979323846f);

    // pitch (y-axis rotation)
    float y0 = 2.0f*(q.w*q.y - q.z*q.x);
    y0 = y0 > 1.0f ? 1.0f : y0;
    y0 = y0 < -1.0f ? -1.0f : y0;
    result.y = asinf(y0)*(180.0f/3.14159265358979323846f);

    // yaw (z-axis rotation)
    float z0 = 2.0f*(q.w*q.z + q.x*q.y);
    float z1 = 1.0f - 2.0f*(q.y*q.y + q.z*q.z);
    result.z = atan2f(z0, z1)*(180.0f/3.14159265358979323846f);

    return result;
}

// Transform a quaternion given a transformation matrix
inline Quaternion QuaternionTransform(Quaternion q, Matrix mat)
{
    Quaternion result = { 0 };

    result.x = mat.m0*q.x + mat.m4*q.y + mat.m8*q.z + mat.m12*q.w;
    result.y = mat.m1*q.x + mat.m5*q.y + mat.m9*q.z + mat.m13*q.w;
    result.z = mat.m2*q.x + mat.m6*q.y + mat.m10*q.z + mat.m14*q.w;
    result.w = mat.m3*q.x + mat.m7*q.y + mat.m11*q.z + mat.m15*q.w;

    return result;
}




// Security check in case no GRAPHICS_API_OPENGL_* defined

        


// Security check in case multiple GRAPHICS_API_OPENGL_* defined
















//----------------------------------------------------------------------------------
// Defines and Macros
//----------------------------------------------------------------------------------

    // This is the maximum amount of elements (quads) per batch
    // NOTE: Be careful with text, every letter maps to a quad
    










// Shader and material limits



// Texture parameters (equivalent to OpenGL defines)


















// Matrix modes (equivalent to OpenGL)




// Primitive assembly draw modes




//----------------------------------------------------------------------------------
// Types and Structures Definition
//----------------------------------------------------------------------------------
typedef enum { OPENGL_11 = 1, OPENGL_21, OPENGL_33, OPENGL_ES_20 } GlVersion;

typedef unsigned char byte;





















































































































































































































































//------------------------------------------------------------------------------------
// Functions Declaration - Matrix operations
//------------------------------------------------------------------------------------
 void rlMatrixMode(int mode);                    // Choose the current matrix to be transformed
 void rlPushMatrix(void);                        // Push the current matrix to stack
 void rlPopMatrix(void);                         // Pop lattest inserted matrix from stack
 void rlLoadIdentity(void);                      // Reset current matrix to identity matrix
 void rlTranslatef(float x, float y, float z);   // Multiply the current matrix by a translation matrix
 void rlRotatef(float angleDeg, float x, float y, float z);  // Multiply the current matrix by a rotation matrix
 void rlScalef(float x, float y, float z);       // Multiply the current matrix by a scaling matrix
 void rlMultMatrixf(float *matf);                // Multiply the current matrix by another matrix
 void rlFrustum(double left, double right, double bottom, double top, double znear, double zfar);
 void rlOrtho(double left, double right, double bottom, double top, double znear, double zfar);
 void rlViewport(int x, int y, int width, int height); // Set the viewport area

//------------------------------------------------------------------------------------
// Functions Declaration - Vertex level operations
//------------------------------------------------------------------------------------
 void rlBegin(int mode);                         // Initialize drawing mode (how to organize vertex)
 void rlEnd(void);                               // Finish vertex providing
 void rlVertex2i(int x, int y);                  // Define one vertex (position) - 2 int
 void rlVertex2f(float x, float y);              // Define one vertex (position) - 2 float
 void rlVertex3f(float x, float y, float z);     // Define one vertex (position) - 3 float
 void rlTexCoord2f(float x, float y);            // Define one vertex (texture coordinate) - 2 float
 void rlNormal3f(float x, float y, float z);     // Define one vertex (normal) - 3 float
 void rlColor4ub(byte r, byte g, byte b, byte a);    // Define one vertex (color) - 4 byte
 void rlColor3f(float x, float y, float z);          // Define one vertex (color) - 3 float
 void rlColor4f(float x, float y, float z, float w); // Define one vertex (color) - 4 float

//------------------------------------------------------------------------------------
// Functions Declaration - OpenGL equivalent functions (common to 1.1, 3.3+, ES2)
// NOTE: This functions are used to completely abstract raylib code from OpenGL layer
//------------------------------------------------------------------------------------
 void rlEnableTexture(unsigned int id);                  // Enable texture usage
 void rlDisableTexture(void);                            // Disable texture usage
 void rlTextureParameters(unsigned int id, int param, int value); // Set texture parameters (filter, wrap)
 void rlEnableRenderTexture(unsigned int id);            // Enable render texture (fbo)
 void rlDisableRenderTexture(void);                      // Disable render texture (fbo), return to default framebuffer
 void rlEnableDepthTest(void);                           // Enable depth test
 void rlDisableDepthTest(void);                          // Disable depth test
 void rlEnableBackfaceCulling(void);                     // Enable backface culling
 void rlDisableBackfaceCulling(void);                    // Disable backface culling
 void rlEnableWireMode(void);                            // Enable wire mode
 void rlDisableWireMode(void);                           // Disable wire mode
 void rlDeleteTextures(unsigned int id);                 // Delete OpenGL texture from GPU
 void rlDeleteRenderTextures(RenderTexture2D target);    // Delete render textures (fbo) from GPU
 void rlDeleteShader(unsigned int id);                   // Delete OpenGL shader program from GPU
 void rlDeleteVertexArrays(unsigned int id);             // Unload vertex data (VAO) from GPU memory
 void rlDeleteBuffers(unsigned int id);                  // Unload vertex data (VBO) from GPU memory
 void rlClearColor(byte r, byte g, byte b, byte a);      // Clear color buffer with color
 void rlClearScreenBuffers(void);                        // Clear used screen buffers (color and depth)
 void rlUpdateBuffer(int bufferId, void *data, int dataSize); // Update GPU buffer with new data
 unsigned int rlLoadAttribBuffer(unsigned int vaoId, int shaderLoc, void *buffer, int size, bool dynamic);   // Load a new attributes buffer

//------------------------------------------------------------------------------------
// Functions Declaration - rlgl functionality
//------------------------------------------------------------------------------------
 void rlglInit(int width, int height);           // Initialize rlgl (buffers, shaders, textures, states)
 void rlglClose(void);                           // De-inititialize rlgl (buffers, shaders, textures)
 void rlglDraw(void);                            // Update and draw default internal buffers

 int rlGetVersion(void);                         // Returns current OpenGL version
 bool rlCheckBufferLimit(int vCount);            // Check internal buffer overflow for a given number of vertex
 void rlSetDebugMarker(const char *text);        // Set debug marker for analysis
 void rlLoadExtensions(void *loader);            // Load OpenGL extensions
 Vector3 rlUnproject(Vector3 source, Matrix proj, Matrix view);  // Get world coordinates from screen coordinates

// Textures data management
 unsigned int rlLoadTexture(void *data, int width, int height, int format, int mipmapCount); // Load texture in GPU
 unsigned int rlLoadTextureDepth(int width, int height, int bits, bool useRenderBuffer);     // Load depth texture/renderbuffer (to be attached to fbo)
 unsigned int rlLoadTextureCubemap(void *data, int size, int format);                        // Load texture cubemap
 void rlUpdateTexture(unsigned int id, int width, int height, int format, const void *data); // Update GPU texture with new data
 void rlGetGlTextureFormats(int format, unsigned int *glInternalFormat, unsigned int *glFormat, unsigned int *glType);  // Get OpenGL internal formats
 void rlUnloadTexture(unsigned int id);                              // Unload texture from GPU memory

 void rlGenerateMipmaps(Texture2D *texture);                         // Generate mipmap data for selected texture
 void *rlReadTexturePixels(Texture2D texture);                       // Read texture pixel data
 unsigned char *rlReadScreenPixels(int width, int height);           // Read screen pixel data (color buffer)

// Render texture management (fbo)
 RenderTexture2D rlLoadRenderTexture(int width, int height, int format, int depthBits, bool useDepthTexture);    // Load a render texture (with color and depth attachments)
 void rlRenderTextureAttach(RenderTexture target, unsigned int id, int attachType);  // Attach texture/renderbuffer to an fbo
 bool rlRenderTextureComplete(RenderTexture target);                 // Verify render texture is complete

// Vertex data management
 void rlLoadMesh(Mesh *mesh, bool dynamic);                          // Upload vertex data into GPU and provided VAO/VBO ids
 void rlUpdateMesh(Mesh mesh, int buffer, int numVertex);            // Update vertex data on GPU (upload new data to one buffer)
 void rlDrawMesh(Mesh mesh, Material material, Matrix transform);    // Draw a 3d mesh with material and transform
 void rlUnloadMesh(Mesh mesh);                                       // Unload mesh data from CPU and GPU

// NOTE: There is a set of shader related functions that are available to end user,
// to avoid creating function wrappers through core module, they have been directly declared in raylib.h

























































/***********************************************************************************
*
*   RLGL IMPLEMENTATION
*
************************************************************************************/




























































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































































