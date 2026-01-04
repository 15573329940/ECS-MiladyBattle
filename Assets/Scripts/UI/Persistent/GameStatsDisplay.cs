using UnityEngine;
using Unity.Entities;
using Unity.Profiling;
using TMPro; // 引入 TextMeshPro

public class GameStatsDisplay : MonoBehaviour
{
    [Header("UI Reference")]
    public TextMeshProUGUI StatsTextElement; // 拖入场景中的 TMP 文本组件

    [Header("Settings")]
    public bool ShowStats = true;
    [Range(0.1f, 2f)] public float UpdateInterval = 0.5f;

    private float _accum = 0f;
    private int _frames = 0;
    private float _timeLeft;

    private EntityManager _entityManager;
    private ProfilerRecorder _mainThreadRecorder;
    private ProfilerRecorder _renderThreadRecorder;
    private ProfilerRecorder _batchesRecorder;

    // 使用 StringBuilder 可以进一步减少微量的 GC，但 0.5s 一次的 string.Format 已经足够快了
    private System.Text.StringBuilder _sb = new System.Text.StringBuilder(256);

    void OnEnable()
    {
        _mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        _renderThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
        _batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 15);
    }

    void OnDisable()
    {
        _mainThreadRecorder.Dispose();
        _renderThreadRecorder.Dispose();
        _batchesRecorder.Dispose();
    }

    void Start()
    {
        _timeLeft = UpdateInterval;
        if (StatsTextElement == null)
        {
            //Debug.LogError("请关联 TextMeshProUGUI 组件！");
            enabled = false;
        }
    }

    void Update()
    {
        if (!ShowStats)
        {
            if (StatsTextElement.enabled) StatsTextElement.enabled = false;
            return;
        }
        if (!StatsTextElement.enabled) StatsTextElement.enabled = true;

        // 累计数据
        _timeLeft -= Time.unscaledDeltaTime;
        _accum += 1.0f / Time.unscaledDeltaTime; // 直接计算 FPS 累加
        ++_frames;

        if (_timeLeft <= 0.0)
        {
            UpdateStatsText();
            _timeLeft = UpdateInterval;
            _accum = 0.0f;
            _frames = 0;
        }
    }

    private void UpdateStatsText()
    {
        float fps = _accum / _frames;
        double mainThreadMs = _mainThreadRecorder.LastValue * (1e-6f);
        double renderThreadMs = _renderThreadRecorder.LastValue * (1e-6f);
        long batches = _batchesRecorder.LastValue;

        int entityCount = 0;
        if (World.DefaultGameObjectInjectionWorld != null)
        {
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            // 这是一个稍微沉重的操作，但每 0.5 秒跑一次可以接受
            entityCount = _entityManager.UniversalQuery.CalculateEntityCount();
        }

        // 使用 StringBuilder 拼接，彻底避免 string.Format 产生的中间垃圾
        _sb.Clear();
        _sb.Append("FPS: ").Append(fps.ToString("F0")).Append("\n");
        _sb.Append("Main CPU: ").Append(mainThreadMs.ToString("F1")).Append(" ms\n");
        _sb.Append("Render: ").Append(renderThreadMs.ToString("F1")).Append(" ms\n");
        _sb.Append("Batches: ").Append(batches).Append("\n");
        _sb.Append("Entities: ").Append(entityCount);

        StatsTextElement.text = _sb.ToString();

        // 动态颜色切换
        if (fps < 30) StatsTextElement.color = Color.red;
        else if (fps < 60) StatsTextElement.color = Color.yellow;
        else StatsTextElement.color = Color.green;
    }

    // 彻底删掉 OnGUI() 函数！
}