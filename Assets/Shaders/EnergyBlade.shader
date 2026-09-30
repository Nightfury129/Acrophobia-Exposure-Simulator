Shader "VRSlashGame/EnergyBlade"
{
    Properties
    {
        [MainColor]   _BaseColor("Energy Tint (Neon Base)", Color) = (0.0, 0.9, 1.0, 1.0)
        [HDR]         _CoreColor("Core Color (Hot White)", Color) = (1.5, 1.5, 1.5, 1.0)
        [HDR]         _EmissionColor("Emission Tint", Color) = (0.0, 0.9, 1.0, 1.0)
        _EmissionIntensity("Emission Intensity", Float) = 1.2

        [Header(Moving Energy Noise Pattern)]
        _ScrollSpeed("Scroll Speed (U, V)", Vector) = (0.0, 4.0, 0.0, 0.0)
        _NoiseScale("Noise Scale (U, V)", Vector) = (3.0, 14.0, 0.0, 0.0)

        [Header(Glow and Fresnel Silhouette)]
        _FresnelPower("Fresnel Rim Power", Range(0.5, 6.0)) = 2.2
        _PulseSpeed("Idle Pulse Speed", Float) = 3.0
        _PulseAmount("Idle Pulse Amount", Range(0.0, 0.5)) = 0.12

        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 0.0
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

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half3  viewDirWS  : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half4  _CoreColor;
                half4  _EmissionColor;
                float  _EmissionIntensity;
                float4 _ScrollSpeed;
                float4 _NoiseScale;
                float  _FresnelPower;
                float  _PulseSpeed;
                float  _PulseAmount;
            CBUFFER_END

            // Mobile VR Optimization: Half-precision (FP16) fast hash & noise for mobile GPU ALU efficiency
            half Hash21(half2 p)
            {
                p = frac(p * half2(123.34h, 456.21h));
                p += dot(p, p + 45.32h);
                return frac(p.x * p.y);
            }

            half Noise2D(half2 p)
            {
                half2 i = floor(p);
                half2 f = frac(p);
                f = f * f * (3.0h - 2.0h * f);

                half a = Hash21(i);
                half b = Hash21(i + half2(1.0h, 0.0h));
                half c = Hash21(i + half2(0.0h, 1.0h));
                half d = Hash21(i + half2(1.0h, 1.0h));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInputs.positionCS;
                output.positionWS = vertexInputs.positionWS;
                output.normalWS = (half3)normalInputs.normalWS;
                output.uv = input.uv;
                output.viewDirWS = (half3)GetWorldSpaceNormalizeViewDir(vertexInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. Animated Dual-Layer Energy Noise in Half Precision (FP16)
                half time = (half)_Time.y;
                half2 scroll = (half2)_ScrollSpeed.xy;
                half2 scale = (half2)_NoiseScale.xy;

                half2 uvLayer1 = (half2)input.uv * scale + scroll * time;
                half2 uvLayer2 = (half2)input.uv * (scale * 1.75h) - scroll * (time * 0.65h);

                half n1 = Noise2D(uvLayer1);
                half n2 = Noise2D(uvLayer2);
                half energyPattern = saturate(n1 * 0.6h + n2 * 0.4h);

                // Sharp electric filaments along wave intersections
                half filaments = pow(saturate(1.0h - abs(n1 - n2) * 2.8h), 2.5h);
                energyPattern = saturate(energyPattern + filaments * 0.55h);

                // 2. Silhouette Fresnel Rim & Core Blend in Half Precision
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(input.viewDirWS);
                half NdotV = saturate(dot(normalWS, viewDirWS));
                half fresnel = pow(1.0h - NdotV, (half)_FresnelPower);

                half3 coreBlend = lerp(_BaseColor.rgb, _CoreColor.rgb, pow(energyPattern, 2.0h) * 0.85h);
                half3 rimGlow = _EmissionColor.rgb * (fresnel * 1.6h + 0.35h);
                half3 energyColor = (coreBlend * (0.65h + energyPattern * 0.7h) + rimGlow);

                // 3. Subtle Idle Breathing Pulse & Velocity Emission Multiplier
                half pulse = 1.0h + sin(time * (half)_PulseSpeed) * (half)_PulseAmount;
                half3 totalEmission = energyColor * (_EmissionColor.rgb * ((half)_EmissionIntensity * pulse));

                // 4. Subtle Ambient / Main Light interaction
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lightDir = (half3)normalize(mainLight.direction);
                half nDotL = saturate(dot(normalWS, lightDir));
                half3 ambientSh = (half3)SampleSH(normalWS) * 0.15h;
                half3 diffuse = (half3)mainLight.color * (nDotL * 0.2h);

                half3 finalRGB = totalEmission + (diffuse + ambientSh) * _BaseColor.rgb;
                return half4(finalRGB, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
