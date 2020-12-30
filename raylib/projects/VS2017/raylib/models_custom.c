#include "../../../raylib.h"

#include <stdlib.h>
#include <string.h>

/*
// Check if config flags have been externally provided on compilation line
#if !defined(EXTERNAL_CONFIG_FLAGS)
#include "../../../config.h"         // Defines module configuration flags
#endif
//*/

#if defined(__cplusplus)
extern "C" {            // Prevents name mangling of functions
#endif

	RLAPI Mesh GenMeshRaw(void* vertices, int vertices_len, void* indices, int indices_len, void* texcoords, int texcoords_len, void* normals, int normals_len, void* colors, int colors_len) {
		Mesh mesh = { 0 };
		mesh.vboId = (unsigned int *)RL_CALLOC(7 * sizeof(unsigned int), 1);

		mesh.vertices = (float *)RL_MALLOC(vertices_len);
		memcpy(mesh.vertices, vertices, vertices_len);

		if (texcoords != NULL) {
			mesh.texcoords = (float *)RL_MALLOC(texcoords_len);
			memcpy(mesh.texcoords, texcoords, texcoords_len);
		}

		if (normals != NULL) {
			mesh.normals = (float *)RL_MALLOC(normals_len);
			memcpy(mesh.normals, normals, normals_len);
		}

		if (indices != NULL) {
			mesh.indices = (unsigned short *)RL_MALLOC(indices_len);
			memcpy(mesh.indices, indices, indices_len);
		}

		if (colors != NULL) {
			mesh.colors = (unsigned char*)RL_MALLOC(colors_len);
			memcpy(mesh.colors, colors, colors_len);
		}

		mesh.vertexCount = vertices_len / (sizeof(float) * 3);
		mesh.triangleCount = mesh.vertexCount / 3;

		rlLoadMesh(&mesh, false);
		return mesh;
	}

#if defined(__cplusplus)
}
#endif