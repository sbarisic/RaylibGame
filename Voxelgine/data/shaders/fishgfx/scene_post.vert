#version 400

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec4 aColor;
layout (location = 2) in vec2 aUv;

out vec4 vColor;
out vec2 vUv;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
	vColor = aColor;
	vUv = aUv;
	gl_Position = uProjection * uView * uModel * vec4(aPosition, 0.0, 1.0);
}
