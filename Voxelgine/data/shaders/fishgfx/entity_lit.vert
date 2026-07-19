#version 430

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec4 aColor;
layout (location = 2) in vec2 aUv;
layout (location = 3) in vec3 aNormal;

out vec4 vColor;
out vec2 vUv;
out vec3 vWorldPosition;
out vec3 vWorldNormal;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
	vec4 world = uModel * vec4(aPosition, 1.0);
	vColor = aColor;
	vUv = aUv;
	vWorldPosition = world.xyz;
	vWorldNormal = normalize(mat3(transpose(inverse(uModel))) * aNormal);
	gl_Position = uProjection * uView * world;
}
