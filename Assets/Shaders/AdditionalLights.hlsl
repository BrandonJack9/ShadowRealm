#ifndef ADDITIONAL_LIGHTS_INCLUDED
#define ADDITIONAL_LIGHTS_INCLUDED

//------------------------------------------------------------------------------------------------------
// Additional Lights
//------------------------------------------------------------------------------------------------------

float SR_Specular(float3 lightDir, float3 normal, float3 viewDir, float focus, float brightness) {
    float3 reflectVec = reflect(-lightDir, normal);
    float RdotV = saturate(dot(reflectVec, viewDir));
    return pow(RdotV, focus) * brightness;
}

float SR_Diffuse(float3 lightDir, float3 normal) {
    return saturate(dot(normal, lightDir));
}

/*
- Calculates light attenuation values to produce multiple bands for a toon effect. See AdditionalLightsToon function below
*/
#ifndef SHADERGRAPH_PREVIEW
float SR_Attenuation(int lightIndex, float3 positionWS, float3 WorldNormal){
	#if !USE_FORWARD_PLUS
		lightIndex = GetPerObjectLightIndex(lightIndex);
	#endif
	#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
		float4 lightPositionWS = _AdditionalLightsBuffer[lightIndex].position;
		half4 spotDirection = _AdditionalLightsBuffer[lightIndex].spotDirection;
		half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[lightIndex].attenuation;
	#else
		float4 lightPositionWS = _AdditionalLightsPosition[lightIndex];
		half4 spotDirection = _AdditionalLightsSpotDir[lightIndex];
		half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[lightIndex];
	#endif

	// Point
	float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
	float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
	float range = rsqrt(distanceAndSpotAttenuation.x);
	float dist = sqrt(distanceSqr) / range;

	// Spot
	half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
	half SdotL = dot(spotDirection.xyz, lightDirection);
	half spotAtten = saturate(SdotL * distanceAndSpotAttenuation.z + distanceAndSpotAttenuation.w);
	spotAtten *= spotAtten;

	// Atten
	bool isSpot = (distanceAndSpotAttenuation.z > 0);
	return isSpot ? 
        spotAtten :
		pow(saturate(1.0 - dist), .5);
}
#endif

/*
- Handles additional lights (e.g. point, spotlights) with simplified Specular lighting
- For shadows to work in the Unlit Graph, the following keywords must be defined in the blackboard :
	- Boolean Keyword, Global Multi-Compile "_ADDITIONAL_LIGHT_SHADOWS"
	- Boolean Keyword, Global Multi-Compile "_ADDITIONAL_LIGHTS" (required to prevent the one above from being stripped from builds)
- For a PBR/Lit Graph, these keywords are already handled for you.
*/
void SR_AdditionalLights_float(int SpecularHighlights, float SpecularFocus, float SpecularBrightness, float3 WorldPosition, float3 WorldNormal, float3 WorldView, half4 Shadowmask,
                            out float Diffuse, out float Specular, out float3 Color) {
    float diffuse = 0;
    float specular = 0;
    float3 color = 0;
#ifndef SHADERGRAPH_PREVIEW
    uint pixelLightCount = GetAdditionalLightsCount();
    uint meshRenderingLayers = GetMeshRenderingLayer();

    float2 uv;

    #if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++) {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            float atten = SR_Attenuation(lightIndex, WorldPosition, WorldNormal) * light.shadowAttenuation;
            float thisDiffuse = SR_Diffuse(light.direction, WorldNormal) * atten;
            diffuse += thisDiffuse;
            float thisSpecular = lerp(0, SR_Specular(light.direction, WorldNormal, WorldView, SpecularFocus, SpecularBrightness) * thisDiffuse, SpecularHighlights);
            specular += thisSpecular;
            color += light.color * (thisDiffuse + thisSpecular);
        }
    }
    #endif

    InputData inputData = (InputData)0;
    float4 screenPos = ComputeScreenPos(TransformWorldToHClip(WorldPosition));
    inputData.normalizedScreenSpaceUV = screenPos.xy / screenPos.w;
    inputData.positionWS = WorldPosition;

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            float atten = SR_Attenuation(lightIndex, WorldPosition, WorldNormal) * light.shadowAttenuation;
            float thisDiffuse = SR_Diffuse(light.direction, WorldNormal) * atten;
            diffuse += thisDiffuse;
            float thisSpecular = lerp(0, SR_Specular(light.direction, WorldNormal, WorldView, SpecularFocus, SpecularBrightness) * thisDiffuse, SpecularHighlights);
            specular += thisSpecular;
            color += light.color * (thisDiffuse + thisSpecular);
        }
    LIGHT_LOOP_END
#endif

    Diffuse = diffuse;
    Specular = specular;
    Color = color;
}

#endif // ADDITIONAL_LIGHTS_INCLUDED