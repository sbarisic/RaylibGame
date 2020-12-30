
#define STBVOX_CONFIG_MODE 0
#define STBVXDEC __declspec(dllexport)

#define STB_VOXEL_RENDER_IMPLEMENTATION 
#include "stb_voxel_render.h"

void  test() {
	int wat = sizeof(stbvox_mesh_maker);

	auto A = stbvox_get_input_description(NULL);
}