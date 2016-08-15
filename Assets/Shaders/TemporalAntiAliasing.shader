Shader "Hidden/Temporal Anti-aliasing"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    CGINCLUDE
    #pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore
    #pragma exclude_renderers gles

    #include "UnityCG.cginc"

    #define TAA_REMOVE_COLOR_SAMPLE_JITTER 1

    #define TAA_USE_EXPERIMENTAL_OPTIMIZATIONS 1

    #define TAA_TONEMAP_COLOR_AND_HISTORY_SAMPLES 1

    #define TAA_COLOR_NEIGHBORHOOD_SAMPLE_PATTERN 1
    #define TAA_COLOR_NEIGHBORHOOD_SAMPLE_SPREAD 1.

    #define TAA_DILATE_MOTION_VECTOR_SAMPLE 1

    #define TAA_CLIP_HISTORY_SAMPLE 1

    #define TAA_HISTORY_NEIGHBORHOOD_SAMPLE_SPREAD 0.5

    #define TAA_DEPTH_SAMPLE_PATTERN 0
    #define TAA_DEPTH_SAMPLE_SPREAD 1.

    #define TAA_SHARPEN_OUTPUT 1
    #define TAA_FINAL_BLEND_METHOD 2

    #if TAA_FINAL_BLEND_METHOD == 0
        #define TAA_FINAL_BLEND_FACTOR .97
    #elif TAA_FINAL_BLEND_METHOD == 2
        #define TAA_FINAL_BLEND_STATIC_FACTOR _FinalBlendParameters.x
        #define TAA_FINAL_BLEND_DYNAMIC_FACTOR _FinalBlendParameters.y
        #define TAA_MOTION_AMPLIFICATION _FinalBlendParameters.z
    #endif

    struct Input
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 vertex : SV_POSITION;

        float2 mainTexUV : TEXCOORD0;
        float2 defaultUV  : TEXCOORD1;

        float4 position : TEXCOORD2;
    };

    struct BlitOptimizedVaryings
    {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Output
    {
        float4 first : SV_Target0;
        float4 second : SV_Target1;
    };

    sampler2D _MainTex;
    sampler2D _HistoryTex;

    sampler2D _BlitSourceTex;

    sampler2D _CameraMotionVectorsTexture;
    sampler2D _CameraDepthTexture;

    float4 _MainTex_TexelSize;
    float4 _HistoryTex_TexelSize;
    float4 _CameraDepthTexture_TexelSize;

    float2 _Jitter;

    #if TAA_SHARPEN_OUTPUT
        float4 _SharpenParameters;
    #endif

    #if TAA_FINAL_BLEND_METHOD == 2
        float4 _FinalBlendParameters;
    #endif

    Varyings vertex(Input input)
    {
        Varyings output;

        float4 vertex = mul(UNITY_MATRIX_MVP, input.vertex);

        output.vertex = vertex;
        output.mainTexUV = input.uv;
        output.defaultUV = input.uv;
        output.position = vertex;

    #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0)
            output.mainTexUV.y = 1. - input.uv.y;
    #endif

        return output;
    }

    #if (SHADER_TARGET < 50 && !defined (SHADER_API_PSSL))
        float rcp(float value)
        {
            return 1. / value;
        }
    #endif

    // Tonemapper from http://gpuopen.com/optimized-reversible-tonemapper-for-resolve/
    float getMaximumElement(in float3 value)
    {
        // AMD_shader_trinary_minmax
        return max(max(value.x, value.y), value.z);
    }

    float4 map(in float4 color)
    {
        return float4(color.rgb * rcp(getMaximumElement(color.rgb) + 1.), color.a);
    }

    float4 map(in float4 color, in float weight)
    {
        return float4(color.rgb * rcp(weight * getMaximumElement(color.rgb) + 1.), color.a);
    }

    float4 unmap(in float4 color)
    {
        return float4(color.rgb * rcp(1. - getMaximumElement(color.rgb)), color.a);
    }

    float2 getClosestFragment(in float2 uv)
    {
        const float2 k = TAA_DEPTH_SAMPLE_SPREAD * _CameraDepthTexture_TexelSize.xy;

        #if TAA_DEPTH_SAMPLE_PATTERN == 0
            const float4 neighborhood = float4(
                tex2D(_CameraDepthTexture, uv - k).r,
                tex2D(_CameraDepthTexture, uv + float2(k.x, -k.y)).r,
                tex2D(_CameraDepthTexture, uv + float2(-k.x, k.y)).r,
                tex2D(_CameraDepthTexture, uv + k).r);

            float3 result = float3(0., 0., tex2D(_CameraDepthTexture, uv).r);

            #if TAA_USE_EXPERIMENTAL_OPTIMIZATIONS
                result.z = min(min(min(neighborhood.x, neighborhood.y), neighborhood.z), neighborhood.w);

                if (result.z == neighborhood.x)
                    result.xy = float2(-1., -1.);
                else if (result.z == neighborhood.y)
                    result.xy = float2(1., -1.);
                else if (result.z == neighborhood.z)
                    result.xy = float2(-1., 1.);
                else
                    result.xy = float2(1., 1.);
            #else
                if (neighborhood.x < result.z)
                    result = float3(-1., -1., neighborhood.x);

                if (neighborhood.y < result.z)
                    result = float3(1., -1., neighborhood.y);

                if (neighborhood.z < result.z)
                    result = float3(-1., 1., neighborhood.z);

                if (neighborhood.w < result.z)
                    result = float3(1., 1., neighborhood.w);
            #endif
        #else
            const float3x3 neighborhood = float3x3(
                tex2D(_CameraDepthTexture, uv - k).r,
                tex2D(_CameraDepthTexture, uv - float2(0., k.y)).r,
                tex2D(_CameraDepthTexture, uv + float2(k.x, -k.y)).r,
                tex2D(_CameraDepthTexture, uv - float2(k.x, 0.)).r,
                tex2D(_CameraDepthTexture, uv).r,
                tex2D(_CameraDepthTexture, uv + float2(k.x, 0.)).r,
                tex2D(_CameraDepthTexture, uv + float2(-k.x, k.y)).r,
                tex2D(_CameraDepthTexture, uv + float2(0., k.y)).r,
                tex2D(_CameraDepthTexture, uv + k).r);

            float3 result = float3(-1., -1., neighborhood._m00);

            if (neighborhood._m01 < result.z)
                result = float3(0., -1., neighborhood._m01);

            if (neighborhood._m02 < result.z)
                result = float3(1., -1., neighborhood._m02);

            if (neighborhood._m10 < result.z)
                result = float3(-1., 0., neighborhood._m10);

            if (neighborhood._m11 < result.z)
                result = float3(0., 0., neighborhood._m11);

            if (neighborhood._m12 < result.z)
                result = float3(1., 0., neighborhood._m12);

            if (neighborhood._m20 < result.z)
                result = float3(-1., 1., neighborhood._m20);

            if (neighborhood._m21 < result.z)
                result = float3(0., 1., neighborhood._m21);

            if (neighborhood._m22 < result.z)
                result = float3(1., 1., neighborhood._m22);
        #endif

        return (uv + result.xy * k);
    }

    // Adapted from Playdead's TAA implementation
    // https://github.com/playdeadgames/temporal
    float4 clipToAABB(in float4 color, in float p, in float3 minimum, in float3 maximum)
    {
        // note: only clips towards aabb center (but fast!)
        float3 center = .5 * (maximum + minimum);
        float3 extents = .5 * (maximum - minimum);

        // This is actually `distance`, however the keyword is reserved
        float4 offset = color - float4(center, p);
        float3 repeat = abs(offset.xyz / extents);

        repeat.x = max(repeat.x, max(repeat.y, repeat.z));

        if (repeat.x > 1.)
        {
            // `color` is not intersecting (nor inside) the AABB; it's clipped to the closest extent
            return float4(center, p) + offset / repeat.x;
        }
        else
        {
            // `color` is intersecting (or inside) the AABB.

            // Note: for whatever reason moving this return statement from this else into a higher
            // scope makes the NVIDIA drivers go beyond bonkers
            return color;
        }
    }

    float4 fragment(Varyings input) : SV_Target
    {
    #if TAA_DILATE_MOTION_VECTOR_SAMPLE
        float2 motion = tex2D(_CameraMotionVectorsTexture, getClosestFragment(input.defaultUV)).xy;
    #else
        float2 motion = tex2D(_CameraMotionVectorsTexture, input.defaultUV).xy;
    #endif

        float2 k = TAA_COLOR_NEIGHBORHOOD_SAMPLE_SPREAD * _MainTex_TexelSize.xy;
        float2 mainUV = input.mainTexUV;
    #if TAA_REMOVE_COLOR_SAMPLE_JITTER && UNITY_UV_STARTS_AT_TOP
        mainUV -= _MainTex_TexelSize.y < 0 ? _Jitter * float2(1, -1) : _Jitter;
    #elif TAA_REMOVE_COLOR_SAMPLE_JITTER
        mainUV -= _Jitter;
    #endif

        float4 color = tex2D(_MainTex, mainUV);

    #if TAA_COLOR_NEIGHBORHOOD_SAMPLE_PATTERN == 0
        // Karis 13: a box filter is not stable under motion, use raw color instead of an averaged one
        float4x4 neighborhood = float4x4(
            tex2D(_MainTex, mainUV + float2(0., -k.y)),
            tex2D(_MainTex, mainUV + float2(-k.x, 0.)),
            tex2D(_MainTex, mainUV + float2(k.x, 0.)),
            tex2D(_MainTex, mainUV + float2(0., k.y)));

        #if TAA_SHARPEN_OUTPUT
            float4 edges = (neighborhood[0] + neighborhood[1] + neighborhood[2] + neighborhood[3]) * .25;
            color += (color - edges) * _SharpenParameters.x;
            color = max(0, color);
        #endif

        #if TAA_CLIP_HISTORY_SAMPLE
            #if TAA_TONEMAP_COLOR_AND_HISTORY_SAMPLES
                float4 average = map(neighborhood[0], .2) + map(neighborhood[1], .2) + map(neighborhood[2], .2) +
                    map(neighborhood[3], .2) + map(color, .2);
            #else
                float4 average = (neighborhood[0] + neighborhood[1] + neighborhood[2] + neighborhood[3] + color) * .2;
            #endif
        #endif

        #if TAA_TONEMAP_COLOR_AND_HISTORY_SAMPLES
            neighborhood[0] = map(neighborhood[0]);
            neighborhood[1] = map(neighborhood[1]);
            neighborhood[2] = map(neighborhood[2]);
            neighborhood[3] = map(neighborhood[3]);

            color = map(color);
        #endif

        float4 minimum = min(min(min(min(neighborhood[0], neighborhood[1]), neighborhood[2]), neighborhood[3]), color);
        float4 maximum = max(max(max(max(neighborhood[0], neighborhood[1]), neighborhood[2]), neighborhood[3]), color);
    #else
        // 0 1 2
        // 3
        float4x4 top = float4x4(
            tex2D(_MainTex, mainUV + float2(-k.x, -k.y)),
            tex2D(_MainTex, mainUV + float2(0., -k.y)),
            tex2D(_MainTex, mainUV + float2(k.x, -k.y)),
            tex2D(_MainTex, mainUV + float2(-k.x, 0.)));

        //     0
        // 1 2 3
        float4x4 bottom = float4x4(
            tex2D(_MainTex, mainUV + float2(k.x, 0.)),
            tex2D(_MainTex, mainUV + float2(-k.x, k.y)),
            tex2D(_MainTex, mainUV + float2(0., k.y)),
            tex2D(_MainTex, mainUV + float2(k.x, k.y)));

        #if TAA_SHARPEN_OUTPUT
            float4 corners = (top[0] + top[2] + bottom[1] + bottom[3]) * .25;
            color += (color - corners) * _SharpenParameters.x;
            color = max(0, color);
        #endif

        #if TAA_CLIP_HISTORY_SAMPLE
            #if TAA_TONEMAP_COLOR_AND_HISTORY_SAMPLES
                float4 average = map(top[0], .111111) + map(top[1], .111111) + map(top[2], .111111) +
                    map(top[3], .111111) + map(color, .111111) + map(bottom[0], .111111) +
                    map(bottom[1], .111111) + map(bottom[2], .111111) + map(bottom[3], .111111);
            #else
                float4 average = (top[0] + top[1] + top[2] + top[3] + bottom[0] + bottom[1] + bottom[2] + bottom[3] + color) * .111111;
            #endif
        #endif

        #if TAA_TONEMAP_COLOR_AND_HISTORY_SAMPLES
            top[0] = map(top[0]);
            top[1] = map(top[1]);
            top[2] = map(top[2]);
            top[3] = map(top[3]);

            color = map(color);

            bottom[0] = map(bottom[0]);
            bottom[1] = map(bottom[1]);
            bottom[2] = map(bottom[2]);
            bottom[3] = map(bottom[3]);
        #endif

        float4 minimum = min(min(min(min(min(min(min(min(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);
        float4 maximum = max(max(max(max(max(max(max(max(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);
    #endif

        k = TAA_HISTORY_NEIGHBORHOOD_SAMPLE_SPREAD * _HistoryTex_TexelSize.xy;
        float2 historyUV = input.defaultUV - motion;

        float4 history = tex2D(_HistoryTex, historyUV);

    #if TAA_TONEMAP_COLOR_AND_HISTORY_SAMPLES
        history = map(history);
    #endif

    #if TAA_CLIP_HISTORY_SAMPLE
        average = clamp(average, minimum, maximum);

        //# History clipping causes artifacts
        history = clipToAABB(history, average.w, minimum.xyz, maximum.xyz);
    #else
        history = clamp(history, minimum, maximum);
    #endif

        float2 luma = float2(Luminance(color.rgb), Luminance(history.rgb));

    #if TAA_FINAL_BLEND_METHOD == 0
        // Constant blend factor, works most of the time & cheap; but isn't as nice as a derivative of Sousa 13
        color = lerp(color, history, TAA_FINAL_BLEND_FACTOR);
    #elif TAA_FINAL_BLEND_METHOD == 1
        // Implements the final blend method from Playdead's TAA implementation
        float weight = 1. - abs(luma.x - luma.y) / max(luma.x, max(luma.y, .2));
        color = lerp(color, history, lerp(.88, .97, weight * weight));
    #elif TAA_FINAL_BLEND_METHOD == 2
        float weight = clamp(lerp(TAA_FINAL_BLEND_STATIC_FACTOR, TAA_FINAL_BLEND_DYNAMIC_FACTOR,
                length(motion * TAA_MOTION_AMPLIFICATION)), TAA_FINAL_BLEND_DYNAMIC_FACTOR, TAA_FINAL_BLEND_STATIC_FACTOR);

        color = lerp(color, history, weight);
    #endif

    #if TAA_TONEMAP_COLOR_AND_HISTORY_SAMPLES
        color = unmap(color);
    #endif

        return color;
    }

    BlitOptimizedVaryings passThrough(in Input input)
    {
        BlitOptimizedVaryings output;

        output.vertex = input.vertex;
        output.uv = .5 * input.vertex + .5;

        return output;
    }

    Output blit(in BlitOptimizedVaryings input)
    {
        Output output;

        float4 color = tex2D(_BlitSourceTex, input.uv);

        output.first = color;
        output.second = color;

        return output;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vertex
            #pragma fragment fragment
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex passThrough
            #pragma fragment blit
            ENDCG
        }
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vertex
            #pragma fragment fragment
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex passThrough
            #pragma fragment blit
            ENDCG
        }
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertex
            #pragma fragment fragment
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex passThrough
            #pragma fragment blit
            ENDCG
        }
    }
}
