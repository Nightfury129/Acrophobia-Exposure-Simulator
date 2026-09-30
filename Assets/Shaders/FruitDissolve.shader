Shader "VRSlashGame/FruitDissolve"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (RGB)", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Header(Dissolve Parameters)]
        _DissolveProgress("Dissolve Progress", Range(0.0, 1.0)) = 0.0
        _SliceOrigin("Slice Origin (Local OS)", Vector) = (0, 0, 0, 0)
        _SliceNormal("Slice Normal (Local OS)", Vector) = (0, 1, 0, 0)
        _ClipSide("Clip Side (-1=Neg, 1=Pos, 0=None)", Float) = 0.0
        _DissolveMaxRadius("Dissolve Max Radius", Float) = 0.65
        _NoiseScale("Noise Scale", Float) = 16.0
        _NoiseStrength("Noise Strength", Range(0.0, 0.5)) = 0.22

        [Header(Glowing Burn Line)]
        _BurnWidth("Burn Edge Width", Range(0.005, 0.15)) = 0.05
        [HDR] _BurnColorHot("Burn Hot Edge (White/Yellow HDR)", Color) = (4.0, 3.8, 2.5, 1.0)
        [HDR] _BurnColorMid("Burn Mid (Glowing Orange HDR)", Color) = (2.5, 0.7, 0.05, 1.0)
        _BurnColorChar("Burn Char (Charred Black)", Color) = (0.05, 0.02, 0.01, 1.0)

        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 0.0 // 0 = Off (Double-Sided)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Cull [_Cull]

        // ------------------------------------------------------------------
        // UniversalForward Pass
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                half3  normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                half3  positionOS   : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float  _DissolveProgress;
                float4 _SliceOrigin;
                float4 _SliceNormal;
                float  _ClipSide;
                float  _DissolveMaxRadius;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _BurnWidth;
                half4  _BurnColorHot;
                half4  _BurnColorMid;
                half4  _BurnColorChar;
            CBUFFER_END

            // Mobile VR Optimization: High-throughput half-precision (FP16) procedural 3D noise
            half Hash31(half3 p)
            {
                p = frac(p * half3(0.3183099h, 0.3183099h, 0.3183099h) + 0.1h);
                p *= 17.0h;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half Noise3D(half3 p)
            {
                half3 i = floor(p);
                half3 f = frac(p);
                f = f * f * (3.0h - 2.0h * f);

                half n000 = Hash31(i + half3(0, 0, 0));
                half n100 = Hash31(i + half3(1, 0, 0));
                half n010 = Hash31(i + half3(0, 1, 0));
                half n110 = Hash31(i + half3(1, 1, 0));
                half n001 = Hash31(i + half3(0, 0, 1));
                half n101 = Hash31(i + half3(1, 0, 1));
                half n011 = Hash31(i + half3(0, 1, 1));
                half n111 = Hash31(i + half3(1, 1, 1));

                half nx00 = lerp(n000, n100, f.x);
                half nx10 = lerp(n010, n110, f.x);
                half nx01 = lerp(n001, n101, f.x);
                half nx11 = lerp(n011, n111, f.x);

                half nxy0 = lerp(nx00, nx10, f.y);
                half nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInputs.positionCS;
                output.positionWS = vertexInputs.positionWS;
                output.normalWS = (half3)normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionOS = (half3)input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. Slice Plane Clipping in Half Precision
                half clipSide = (half)_ClipSide;
                half3 sliceOrigin = (half3)_SliceOrigin.xyz;
                half3 sliceNormal = (half3)_SliceNormal.xyz;

                if (clipSide > 0.5h)
                {
                    half planeDist = dot(input.positionOS - sliceOrigin, sliceNormal);
                    clip(planeDist);
                }
                else if (clipSide < -0.5h)
                {
                    half planeDist = dot(input.positionOS - sliceOrigin, sliceNormal);
                    clip(-planeDist);
                }

                // 2. Dissolve Animation with Half-Precision Edge Math
                half3 emissionGlow = half3(0, 0, 0);
                half progress = (half)_DissolveProgress;

                if (progress > 0.0001h)
                {
                    half distToSlice = length(input.positionOS - sliceOrigin);
                    half noise = Noise3D(input.positionOS * (half)_NoiseScale);
                    half noisyDist = distToSlice + (noise - 0.5h) * (half)_NoiseStrength;

                    half maxReach = (half)_DissolveMaxRadius + (half)_NoiseStrength * 0.5h + (half)_BurnWidth;
                    half cutoffRadius = progress * maxReach;

                    half edgeDist = noisyDist - cutoffRadius;
                    clip(edgeDist); // Discard dissolved material

                    // Evaluate burning line gradient in half precision
                    half burnWidth = max(0.0001h, (half)_BurnWidth);
                    if (edgeDist < burnWidth)
                    {
                        half t = saturate(edgeDist / burnWidth);
                        if (t < 0.25h)
                        {
                            half subT = t * 4.0h;
                            emissionGlow = lerp(_BurnColorHot.rgb, _BurnColorMid.rgb, subT);
                        }
                        else if (t < 0.65h)
                        {
                            half subT = (t - 0.25h) * 2.5h;
                            emissionGlow = lerp(_BurnColorMid.rgb, _BurnColorChar.rgb, subT);
                        }
                        else
                        {
                            half subT = (t - 0.65h) * 2.857h;
                            emissionGlow = lerp(_BurnColorChar.rgb, half3(0, 0, 0), subT);
                        }
                    }
                }

                // 3. Diffuse Lighting & Base Color in Half Precision
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalWS = normalize(input.normalWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lightDir = (half3)normalize(mainLight.direction);
                half nDotL = saturate(dot(normalWS, lightDir));
                half3 diffuse = (half3)mainLight.color * (nDotL * (half)mainLight.shadowAttenuation);
                half3 ambient = (half3)SampleSH(normalWS);

                half3 finalColor = baseTex.rgb * (diffuse + ambient) + emissionGlow;
                return half4(finalColor, baseTex.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // ShadowCaster Pass (Mobile Optimized: Fast Planar/Radial Dissolve Cutoff)
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                half3  positionOS   : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float  _DissolveProgress;
                float4 _SliceOrigin;
                float4 _SliceNormal;
                float  _ClipSide;
                float  _DissolveMaxRadius;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _BurnWidth;
                half4  _BurnColorHot;
                half4  _BurnColorMid;
                half4  _BurnColorChar;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionOS = (half3)input.positionOS.xyz;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                positionCS = ApplyShadowClamping(positionCS);
                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half clipSide = (half)_ClipSide;
                half3 sliceOrigin = (half3)_SliceOrigin.xyz;
                half3 sliceNormal = (half3)_SliceNormal.xyz;

                if (clipSide > 0.5h)
                {
                    clip(dot(input.positionOS - sliceOrigin, sliceNormal));
                }
                else if (clipSide < -0.5h)
                {
                    clip(-dot(input.positionOS - sliceOrigin, sliceNormal));
                }

                // Mobile VR Optimization: Fast radial dissolve without 8-point 3D noise lattice
                // Eliminates dozens of transcendentals in shadow passes
                half progress = (half)_DissolveProgress;
                if (progress > 0.0001h)
                {
                    half distToSlice = length(input.positionOS - sliceOrigin);
                    half maxReach = (half)_DissolveMaxRadius + (half)_BurnWidth;
                    clip(distToSlice - progress * maxReach);
                }

                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DepthOnly Pass (Mobile Optimized)
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                half3  positionOS   : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float  _DissolveProgress;
                float4 _SliceOrigin;
                float4 _SliceNormal;
                float  _ClipSide;
                float  _DissolveMaxRadius;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _BurnWidth;
                half4  _BurnColorHot;
                half4  _BurnColorMid;
                half4  _BurnColorChar;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionOS = (half3)input.positionOS.xyz;
                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInputs.positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half clipSide = (half)_ClipSide;
                half3 sliceOrigin = (half3)_SliceOrigin.xyz;
                half3 sliceNormal = (half3)_SliceNormal.xyz;

                if (clipSide > 0.5h)
                {
                    clip(dot(input.positionOS - sliceOrigin, sliceNormal));
                }
                else if (clipSide < -0.5h)
                {
                    clip(-dot(input.positionOS - sliceOrigin, sliceNormal));
                }

                half progress = (half)_DissolveProgress;
                if (progress > 0.0001h)
                {
                    half distToSlice = length(input.positionOS - sliceOrigin);
                    half maxReach = (half)_DissolveMaxRadius + (half)_BurnWidth;
                    clip(distToSlice - progress * maxReach);
                }

                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
