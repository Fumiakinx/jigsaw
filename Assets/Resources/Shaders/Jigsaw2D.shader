Shader "Custom/Jigsaw2D"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _EdgeDarkness ("Edge Darkness", Range(0, 2)) = 1.0
        _SpecularStrength ("Specular Strength", Range(0, 5)) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Stencil
        {
            Ref 128
            Pass Replace
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // UV2の代わりに頂点カラーを使用
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 edgeData : TEXCOORD1; // x: influence, yz: world edge direction
            };

            sampler2D _MainTex;
            float _EdgeDarkness;
            float _SpecularStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.edgeData.x = v.color.a; // Edge influence
                
                // Color.rg は 0~1 にマッピングされているため、-1~1に戻す
                float2 localDir = v.color.rg * 2.0 - 1.0;
                // WebGL互換性を高めるため、キャストを避け、明示的なベクトル乗算を行う
                float3 worldDir = mul(unity_ObjectToWorld, float4(localDir, 0, 0)).xyz;
                o.edgeData.yz = normalize(worldDir.xy);
                o.edgeData.w = 0;
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                
                float2 edgeGradient = i.edgeData.yz;
                
                // 光源方向 (左上)
                float2 lightDir = normalize(float2(-1.0, 1.0));
                float lightIntensity = dot(edgeGradient, lightDir);
                
                // --- 立体感（ベベル）の「光と影」の分離・超強化 ---
                // 1. 光用の鋭いカーブ（3乗）：ハイライトを細く鋭く光らせる
                float highlightCurve = pow(max(0.001, i.edgeData.x), 3.0); 
                
                // 2. 影用の緩やかなカーブ（1.3乗）：影の幅をさらに広げ、人間の目にしっかり見せる
                float shadowCurve = pow(max(0.001, i.edgeData.x), 1.3); 
                
                // 3. 切断面（溝）：一番外側の極めて狭い範囲を「細い黒線」にする
                float cutEdge = smoothstep(0.95, 1.0, i.edgeData.x);
                
                // 光の当たり具合 (ハイライト面)
                float facingLight = max(0, lightIntensity);
                
                // 影の当たり具合 (光源と反対側であるシャドウ面：0〜1)
                float shadowFactor = max(0.0, -lightIntensity);
                
                // 光の強さ(加算)に対抗するため、影のパワーをさらに限界突破。
                // 影用の緩やかなカーブ(shadowCurve)を使い、光源の反対側はガッツリと広く深く暗くする
                float shadowAmount = shadowCurve * _EdgeDarkness * (0.5 + shadowFactor * 1.5);
                
                // エッジのシャドウ：丸みによる自然な陰影 ＋ 溝の鋭い黒線
                float edgeShadow = 1.0 - shadowAmount - (cutEdge * _EdgeDarkness * 1.5);
                
                // 極限まで黒いリアルな影を表現するため、減算リミッターを 0.01 までほぼ全開放
                edgeShadow = max(0.01, edgeShadow);
                
                // ハイライト：反射を鋭くしつつ、WebGLでの真っ暗バグを防ぐため max(0.001) で保護
                float spec = pow(max(0.001, facingLight), 3.0);
                
                // ハイライトには鋭い方のカーブ(highlightCurve)を適用
                float highlight = spec * highlightCurve * (1.0 - cutEdge) * _SpecularStrength * 0.8;
                
                // 最終的な色合成
                col.rgb = col.rgb * edgeShadow + float3(0.95, 0.95, 1.0) * highlight;
                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }
}
