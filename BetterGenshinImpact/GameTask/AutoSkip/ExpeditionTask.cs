﻿using BetterGenshinImpact.GameTask.Common;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Sdcb.PaddleOCR;
using System.Xml.Linq;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/// <summary>
/// 重新探索派遣
///
/// 必须在已经有探索派遣完成的情况下使用
/// </summary>
public class ExpeditionTask
{
    private static readonly List<string> ExpeditionCharacterList = new() { "菲谢尔", "班尼特" };

    //private static readonly List<string> GameAreaNames = new() { "蒙德", "璃月", "稻妻", "须弥", "枫丹" };

    //private static readonly List<Point> GameAreaNamePoints = new() { new Point(140,165), new Point(140, 235), new Point(140, 305), new Point(140, 165), new Point(140, 165) };

    //private static readonly Point FirstGameAreaNamePoint = new(140, 165);

    private int _expeditionCount = 0;

    public void Run(CaptureContent content)
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        ReExplorationGameArea(content);
        for (var i = 0; i <= 4; i++)
        {
            if (_expeditionCount >= 5)
            {
                // 最多派遣5人
                break;
            }
            else
            {
                TaskControl.Sleep(500);
                content.CaptureRectArea
                    .Derive(new Rect((int)(110 * assetScale), (int)((145 + 70 * i) * assetScale),
                        (int)(60 * assetScale), (int)(33 * assetScale)))
                    .ClickCenter();
                ReExplorationGameArea(content);
            }
        }

        VisionContext.Instance().DrawContent.ClearAll();
    }

    private void ReExplorationGameArea(CaptureContent content)
    {
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        for (var i = 0; i < 5; i++)
        {
            var result = CaptureAndOcr(content, new Rect(0, 0, captureRect.Width - (int)(480 * assetScale), captureRect.Height));
            var rect = result.FindRectByText("探险完成");
            if (rect != Rect.Empty)
            {
                // 点击探险完成
                content.CaptureRectArea.Derive(new Rect(rect.X, rect.Y + (int)(50 * assetScale), rect.Width, (int)(80 * assetScale))).ClickCenter();
                // 重新截图 找领取
                result = CaptureAndOcr(content);
                rect = result.FindRectByText("领取");
                if (rect != Rect.Empty)
                {
                    using var ra = content.CaptureRectArea.Derive(rect);
                    ra.ClickCenter();
                    TaskControl.Logger.LogInformation("探索派遣：点击{Text}", "领取");
                    TaskControl.Sleep(300);
                    // 点击空白区域继续
                    ra.ClickCenter();
                    TaskControl.Sleep(200);

                    // 选择角色
                    result = CaptureAndOcr(content);
                    rect = result.FindRectByText("选择角色");
                    if (rect != Rect.Empty)
                    {
                        content.CaptureRectArea.Derive(rect).ClickCenter();
                        TaskControl.Sleep(800); // 等待动画
                        var success = SelectCharacter(content);
                        if (success)
                        {
                            _expeditionCount++;
                        }
                    }
                }
                else
                {
                    TaskControl.Logger.LogWarning("探索派遣：找不到 {Text} 文字", "领取");
                }
            }
            else
            {
                break;
            }
        }
    }

    private bool SelectCharacter(CaptureContent content)
    {
        var result = CaptureAndOcr(content);
        if (result.RegionHasText("角色选择"))
        {
            var cards = GetCharacterCards(result);
            if (cards.Count > 0)
            {
                var card = cards.First(c => c.Idle);
                var rect = card.Rects.First();

                using var ra = content.CaptureRectArea.Derive(rect);
                ra.ClickCenter();
                TaskControl.Logger.LogInformation("探索派遣：选择角色 {Name}", card.Name);
                TaskControl.Sleep(500);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 根据文字识别结果 获取所有角色选项
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private List<ExpeditionCharacterCard> GetCharacterCards(PaddleOcrResult result)
    {
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;


        var ocrResultRects = result.Regions.Select(x => x.ToOcrResultRect()).ToList();
        ocrResultRects = ocrResultRects.Where(r => r.Rect.X + r.Rect.Width < captureRect.Width / 2)
            .OrderBy(r => r.Rect.Y).ThenBy(r => r.Rect.X).ToList();


        var cards = new List<ExpeditionCharacterCard>();
        foreach (var ocrResultRect in ocrResultRects)
        {
            if (ocrResultRect.Text.Contains("时间缩短") || ocrResultRect.Text.Contains("奖励增加") || ocrResultRect.Text.Contains("暂无加成"))
            {
                var card = new ExpeditionCharacterCard();
                card.Rects.Add(ocrResultRect.Rect);
                card.Addition = ocrResultRect.Text;
                foreach (var ocrResultRect2 in ocrResultRects)
                {
                    if (ocrResultRect2.Rect.Y > ocrResultRect.Rect.Y - 50 * assetScale
                        && ocrResultRect2.Rect.Y + ocrResultRect2.Rect.Height > ocrResultRect.Rect.Y)
                    {
                        if (ocrResultRect2.Text.Contains("探险完成"))
                        {
                            card.Idle = false;
                            var name = ocrResultRect2.Text.Replace("探险完成", "").Replace("/", "").Trim();
                            if (!string.IsNullOrEmpty(name))
                            {
                                card.Name = name;
                            }
                        }
                        else
                        {
                            card.Name = ocrResultRect2.Text;
                        }

                        card.Rects.Add(ocrResultRect.Rect);
                    }
                }

                cards.Add(card);
            }
        }

        return cards;
    }

    private readonly Pen _pen = new(Color.Red, 1);

    private PaddleOcrResult CaptureAndOcr(CaptureContent content)
    {
        using var bitmap = TaskControl.CaptureGameBitmap(content.Dispatcher.GameCapture);
        using var mat = bitmap.ToMat();
        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2GRAY);
        var result = OcrFactory.Paddle.OcrResult(mat);
        VisionContext.Instance().DrawContent.PutOrRemoveRectList("OcrResultRects", result.ToRectDrawableList());
        return result;
    }


    private PaddleOcrResult CaptureAndOcr(CaptureContent content, Rect rect)
    {
        using var bitmap = TaskControl.CaptureGameBitmap(content.Dispatcher.GameCapture);
        using var mat = new Mat(bitmap.ToMat(), rect);
        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2GRAY);
        var result = OcrFactory.Paddle.OcrResult(mat);
        VisionContext.Instance().DrawContent.PutOrRemoveRectList("OcrResultRects", result.ToRectDrawableList(_pen));
        return result;
    }
}