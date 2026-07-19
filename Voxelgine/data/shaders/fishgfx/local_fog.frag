#version 430

in vec2 vUv;
layout (location = 0) out vec4 outColor;

uniform sampler2D uTexture;
uniform sampler2D uSceneDepth;
uniform sampler3D uFogVolume;
uniform mat4 uInverseViewProjection;
uniform vec3 uCameraPosition;
uniform vec3 uFogOrigin;
uniform vec3 uFogSize;
uniform float uStepLength;
uniform int uMaximumSteps;
uniform float uJitter;

bool intersectBox(vec3 origin, vec3 direction, vec3 minimum, vec3 maximum,
	out float entry, out float exitDistance)
{
	vec3 safeDirection = vec3(
		abs(direction.x) < 0.000001 ? (direction.x < 0.0 ? -0.000001 : 0.000001) : direction.x,
		abs(direction.y) < 0.000001 ? (direction.y < 0.0 ? -0.000001 : 0.000001) : direction.y,
		abs(direction.z) < 0.000001 ? (direction.z < 0.0 ? -0.000001 : 0.000001) : direction.z
	);
	vec3 reciprocal = 1.0 / safeDirection;
	vec3 first = (minimum - origin) * reciprocal;
	vec3 second = (maximum - origin) * reciprocal;
	vec3 nearValue = min(first, second);
	vec3 farValue = max(first, second);
	entry = max(max(nearValue.x, nearValue.y), nearValue.z);
	exitDistance = min(min(farValue.x, farValue.y), farValue.z);
	return exitDistance >= max(entry, 0.0);
}

vec3 reconstructWorld(vec2 uv, float depth)
{
	vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
	vec4 world = uInverseViewProjection * clip;
	return world.xyz / world.w;
}

void main()
{
	vec4 scene = texture(uTexture, vUv);
	float depth = texture(uSceneDepth, vUv).r;
	vec3 farPoint = reconstructWorld(vUv, 1.0);
	vec3 direction = normalize(farPoint - uCameraPosition);
	float entry;
	float exitDistance;
	if (!intersectBox(uCameraPosition, direction, uFogOrigin,
		uFogOrigin + uFogSize, entry, exitDistance))
	{
		outColor = scene;
		return;
	}

	if (depth < 0.999999)
	{
		vec3 surface = reconstructWorld(vUv, depth);
		exitDistance = min(exitDistance, distance(uCameraPosition, surface));
	}

	entry = max(entry, 0.0);
	if (exitDistance <= entry)
	{
		outColor = scene;
		return;
	}

	float distanceAlongRay = entry + fract(uJitter) * uStepLength;
	vec3 accumulated = vec3(0.0);
	float transmittance = 1.0;
	for (int step = 0; step < uMaximumSteps && distanceAlongRay < exitDistance; step++)
	{
		vec3 worldPosition = uCameraPosition + direction * distanceAlongRay;
		vec3 coordinate = (worldPosition - uFogOrigin) / uFogSize;
		vec4 sampleValue = texture(uFogVolume, coordinate);
		float density = sampleValue.a;
		if (density > 0.00001)
		{
			vec3 sourceColor = sampleValue.rgb / density;
			float opacity = 1.0 - exp(-density * uStepLength);
			accumulated += transmittance * sourceColor * opacity;
			transmittance *= 1.0 - opacity;
			if (transmittance < 0.01)
			{
				break;
			}
		}
		distanceAlongRay += uStepLength;
	}

	outColor = vec4(accumulated + scene.rgb * transmittance, scene.a);
}
