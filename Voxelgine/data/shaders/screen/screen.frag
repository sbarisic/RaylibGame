#version 330

#define FXAA_REDUCE_MIN (1.0/128.0)
#define FXAA_REDUCE_MUL (1.0/8.0)
#define FXAA_SPAN_MAX 8.0

in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

uniform sampler2D texture0;
uniform vec2 resolution;

float pixelWidth = 5.0;
float pixelHeight = 5.0;

vec4 calc_fxaa(vec2 fragTexCoord2) {
    vec2 inverse_resolution = vec2(1.0 / resolution.x, 1.0 / resolution.y);
    
    vec3 rgbNW = texture2D(texture0, fragTexCoord2.xy + (vec2(-1.0, -1.0)) * inverse_resolution).xyz;
    vec3 rgbNE = texture2D(texture0, fragTexCoord2.xy + (vec2(1.0, -1.0)) * inverse_resolution).xyz;
    vec3 rgbSW = texture2D(texture0, fragTexCoord2.xy + (vec2(-1.0, 1.0)) * inverse_resolution).xyz;
    vec3 rgbSE = texture2D(texture0, fragTexCoord2.xy + (vec2(1.0, 1.0)) * inverse_resolution).xyz;
    
    vec3 rgbM  = texture2D(texture0, fragTexCoord2.xy).xyz;
    
    vec3 luma = vec3(0.299, 0.587, 0.114);

    float lumaNW = dot(rgbNW, luma);
    float lumaNE = dot(rgbNE, luma);
    float lumaSW = dot(rgbSW, luma);
    float lumaSE = dot(rgbSE, luma);
    float lumaM  = dot(rgbM,  luma);
    float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
    float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE))); 

    vec2 dir;
    dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
    dir.y = ((lumaNW + lumaSW) - (lumaNE + lumaSE));

    float dirReduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * (0.25 * FXAA_REDUCE_MUL), FXAA_REDUCE_MIN);
    float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);

    dir = min(vec2(FXAA_SPAN_MAX, FXAA_SPAN_MAX),max(vec2(-FXAA_SPAN_MAX, -FXAA_SPAN_MAX),dir * rcpDirMin)) * inverse_resolution;

    vec3 rgbA = 0.5 * (texture2D(texture0, fragTexCoord2.xy + dir * (1.0 / 3.0 - 0.5)).xyz + texture2D(texture0, fragTexCoord2.xy + dir * (2.0 / 3.0 - 0.5)).xyz);
    vec3 rgbB = rgbA * 0.5 + 0.25 * (texture2D(texture0, fragTexCoord2.xy + dir * -0.5).xyz + texture2D(texture0, fragTexCoord2.xy + dir * 0.5).xyz);
    
    float lumaB = dot(rgbB, luma);

    vec4 fxaaColor = vec4(0, 0, 0, 1);

    if((lumaB < lumaMin) || (lumaB > lumaMax)) 
    {
        fxaaColor = vec4(rgbA, 1.0);
    } 
    else 
    {
        fxaaColor = vec4(rgbB, 1.0);
    }

    return fxaaColor;
}
 
vec3 color_palette(vec3 clr) {
    clr = clr * 255;
    //clr = floor(clr / 8) * 8;
    return clr / 255;
}

void main()
{
    vec4 out_clr = calc_fxaa(fragTexCoord);

    finalColor = vec4(color_palette(out_clr.rgb), out_clr.a);
    gl_FragDepth = 1.0 - finalColor.z;
    
    // Pixelate filter
    /*vec2 inverse_resolution = vec2(1.0 / resolution.x, 1.0 / resolution.y);

    float dx = pixelWidth * inverse_resolution.x;
    float dy = pixelHeight * inverse_resolution.y;
    vec2 coord = vec2(dx * floor(fragTexCoord.x / dx), dy * floor(fragTexCoord.y / dy));
    
    finalColor = texture2D(texture0, coord);*/
    
}
