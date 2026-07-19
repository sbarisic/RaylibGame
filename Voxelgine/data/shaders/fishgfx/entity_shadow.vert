#version 430

layout (location = 0) in vec3 aPosition;
layout (location = 2) in vec2 aUv;

out vec2 vUv;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
	vUv = aUv;
	gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
}
