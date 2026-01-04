using UnityEditor;
using UnityEngine;

public class UIAnchorTool : UnityEditor.Editor
{
    // 定义菜单项和快捷键
    // "Tools/Fit Anchors to Rect" 是菜单路径
    // " &a" 代表快捷键 Alt + A (在 Mac 上是 Option + A)
    [MenuItem("Tools/Fit Anchors to Rect &a")]
    static void FitAnchors()
    {
        // 遍历所有选中的物体，支持批量操作
        foreach (GameObject go in Selection.gameObjects)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            
            // 如果不是 UI 元素或者没有父物体，跳过
            if (rect == null || rect.parent == null)
            {
                continue;
            }

            RectTransform parentRect = rect.parent as RectTransform;
            if (parentRect == null)
            {
                continue;
            }

            // 记录操作以便可以撤销 (Ctrl+Z)
            Undo.RecordObject(rect, "Fit Anchors");

            // 获取父物体的宽高
            float parentWidth = parentRect.rect.width;
            float parentHeight = parentRect.rect.height;

            // 防止除以0的错误
            if (parentWidth == 0 || parentHeight == 0)
            {
                continue;
            }

            // --- 核心算法 ---
            // 目标：将当前的 offset (像素) 转化为 anchor (比例)
            
            Vector2 newAnchorMin = rect.anchorMin;
            Vector2 newAnchorMax = rect.anchorMax;

            // offsetMin 对应 Left 和 Bottom
            // offsetMax 对应 Right 和 Top
            // 公式：新锚点 = 旧锚点 + (像素偏移 / 父物体尺寸)
            
            newAnchorMin.x += rect.offsetMin.x / parentWidth;
            newAnchorMin.y += rect.offsetMin.y / parentHeight;
            
            newAnchorMax.x += rect.offsetMax.x / parentWidth;
            newAnchorMax.y += rect.offsetMax.y / parentHeight;

            // 应用新的锚点
            rect.anchorMin = newAnchorMin;
            rect.anchorMax = newAnchorMax;

            // 将 Offset 归零，因为位置已经由锚点决定了
            // 这样物体在视觉上位置不变，但锚点已经吸附到边框上了
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}