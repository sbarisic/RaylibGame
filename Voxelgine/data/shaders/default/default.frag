#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragPosition;
in vec3 fragNormal;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;

// Output fragment color
layout (location = 0) out vec4 finalColor;
layout (location = 1) out vec4 vertPosition;
layout (location = 2) out vec4 vertNormal;
layout (location = 3) out vec4 vertAlbedo;

// NOTE: Add your custom variables here

void main()
{
    // Texel color fetching from texture sampler
    vec4 texelColor = texture(texture0, fragTexCoord);

    // NOTE: Implement here your fragment shader code

    // final color is the color from the texture 
    //    times the tint color (colDiffuse)
    //    times the fragment color (interpolated vertex color)
    //finalColor = texelColor * colDiffuse * fragColor * vec4(1, 0, 0, 1);

    finalColor = texelColor * colDiffuse  * fragColor;

    vertPosition = vec4(fragPosition, 0);
    vertNormal = vec4(fragNormal, 0);
    vertAlbedo = vec4(finalColor.rgb, 0);

    //gl_FragDepth = 1.0 - finalColor.z;
}