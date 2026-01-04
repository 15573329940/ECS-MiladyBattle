using UnityEngine;
using UnityEngine.EventSystems;

namespace TMG.NFE_Tutorial
{
    public class MinimapUISync : UIBehaviour
    {
        // xy = 屏幕左下角坐标, zw = 宽高
        public static Vector4 ScreenRectVector; 
        
        private RectTransform _rectTransform;
        private Canvas _canvas;
        
        // 记录上一帧的分辨率，用于检测变化
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        protected override void Awake()
        {
            base.Awake();
            _canvas = GetComponentInParent<Canvas>();
            _rectTransform = GetComponent<RectTransform>();
            ForceRecalculate();
        }

        // 1. 保留这个回调，处理普通的 UI 动画或锚点变化
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (gameObject.activeInHierarchy) ForceRecalculate();
        }

        // 2. 【核心修复】在 Update 中每帧检查
        // 虽然每帧计算听起来浪费，但 GetWorldCorners 开销极小，
        // 相比于由分辨率切换导致的显示错误，这点开销完全值得。
        private void Update()
        {
            // 如果分辨率变了，或者 RectTransform 这一帧发生了位移（CanvasScaler 调整了它）
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight || transform.hasChanged)
            {
                ForceRecalculate();
                transform.hasChanged = false; // 重置标记
            }
        }

        private void ForceRecalculate()
        {
            if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return; // 还没初始化好

            // 记录当前分辨率
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            // --- 强制刷新 Canvas 布局 (可选) ---
            // 在极少数情况下，分辨率刚变时 RectTransform 还没更新。
            // 如果发现依然不对，可以取消下面这行的注释，强制 UI 立即对齐
            // Canvas.ForceUpdateCanvases();

            // 1. 获取四个角的“世界”坐标
            Vector3[] corners = new Vector3[4];
            _rectTransform.GetWorldCorners(corners);

            // 2. 将世界坐标转换为屏幕像素坐标
            Camera uiCam = null;
            if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCam = _canvas.worldCamera;
            }

            Vector2 screenBotLeft = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
            Vector2 screenTopRight = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);

            // 3. 计算基础宽高
            float x = screenBotLeft.x;
            float y = screenBotLeft.y;
            float w = screenTopRight.x - screenBotLeft.x;
            float h = screenTopRight.y - screenBotLeft.y;

            // 4. 【强制正方形逻辑】以高度为基准
            float finalSize = h; 
            
            // 修正：如果强制正方形，且 UI 锚点是中心对齐，我们需要重新计算 X 轴的起始点
            // 让绘制区域在 UI 框内居中
            float widthDiff = w - finalSize;
            x += widthDiff * 0.5f; 

            // 赋值
            ScreenRectVector = new Vector4(x, y, finalSize, finalSize);
        }
    }
}