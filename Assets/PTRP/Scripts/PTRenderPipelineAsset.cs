using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
[CustomEditor(typeof(PTRenderPipelineAsset))]
public class PTRenderPipelineAssetEditor : Editor {
    bool restirFoldout = false;
    bool accumulationFoldout = false;
    bool debugFoldout = false;
    public override void OnInspectorGUI() {
        serializedObject.Update();

        PTRenderPipelineAsset rp = target as PTRenderPipelineAsset;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("_MaxBounces"));

        restirFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(restirFoldout, "ReSTIR");
        if (restirFoldout) {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_TemporalReuse"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_SpatialReusePasses"));
            if (rp._SpatialReusePasses > 0) {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_SpatialReuseSamples"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_SpatialReuseRadius"));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_MCap"));
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        accumulationFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(accumulationFoldout, "Accumulation");
        if (accumulationFoldout) {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_TargetSampleCount"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_DepthReuseCutoff"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_NormalReuseCutoff"));
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        debugFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(debugFoldout, "Debug");
        if (debugFoldout) {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_ReuseX"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_DebugCounters"));
            if (rp._DebugCounters) {
                EditorGUILayout.LabelField("Rays/pixel", (rp._DebugCounterData[(int)DebugCounterType.RAYS]/rp._DebugCounterPixels).ToString("f2"));
                EditorGUILayout.LabelField("Shadow rays/pixel", (rp._DebugCounterData[(int)DebugCounterType.SHADOW_RAYS]/rp._DebugCounterPixels).ToString("f2"));
                EditorGUILayout.LabelField("Shifts/pixel", (rp._DebugCounterData[(int)DebugCounterType.SHIFT_ATTEMPTS]/rp._DebugCounterPixels).ToString("f2"));
                EditorGUILayout.LabelField("Shift success", (100f*rp._DebugCounterData[(int)DebugCounterType.SHIFT_SUCCESSES]/rp._DebugCounterData[(int)DebugCounterType.SHIFT_ATTEMPTS]).ToString("f1") + "%");
                EditorGUILayout.LabelField("Reconnections/pixel", (rp._DebugCounterData[(int)DebugCounterType.RECONNECTION_ATTEMPTS]/rp._DebugCounterPixels).ToString("f2"));
                EditorGUILayout.LabelField("Reconnection success" + (100f*rp._DebugCounterData[(int)DebugCounterType.RECONNECTION_SUCCESSES]/rp._DebugCounterData[(int)DebugCounterType.RECONNECTION_ATTEMPTS]).ToString("f1") + "%");
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        serializedObject.ApplyModifiedProperties();
    }
}
#endif

enum DebugCounterType {
    RAYS,
    SHADOW_RAYS,

    SHIFT_ATTEMPTS,
    SHIFT_SUCCESSES,

    RECONNECTION_ATTEMPTS,
    RECONNECTION_SUCCESSES,

    NUM_DEBUG_COUNTERS
};

[CreateAssetMenu(menuName = "Rendering/PTRenderPipelineAsset")]
public class PTRenderPipelineAsset : RenderPipelineAsset {
    [Header("Path tracing")]
    public uint _MaxBounces = 2;

    public bool _TemporalReuse = false;
    public uint _SpatialReusePasses = 2;
    public uint _SpatialReuseSamples = 1;
    public float _SpatialReuseRadius = 64;
    public float _MCap = 10;

    public uint _TargetSampleCount = 0;
    public float _DepthReuseCutoff = 0.01f;
    public float _NormalReuseCutoff = 3;
    
    public float _ReuseX = 0;
    public bool _DebugCounters = false;
    public int[] _DebugCounterData = new int[(int)DebugCounterType.NUM_DEBUG_COUNTERS];
    public float _DebugCounterPixels;
    
    protected override RenderPipeline CreatePipeline() {
        return new PTRenderPipeline(this);
    }
}
