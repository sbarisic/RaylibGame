#version 430

in vec2 vUv;

uniform sampler2D uTexture;
uniform float uAlphaCutoff;

void main()
{
	if (texture(uTexture, vUv).a < uAlphaCutoff)
	{
		discard;
	}
}
