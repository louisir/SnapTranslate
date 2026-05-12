using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapTranslate.Services;

public sealed class UiAutomationPointTextCaptureEngine : IPointTextCaptureEngine
{
    private const int MaxElementTextLength = 160;
    private const int MaxTokenDistance = 32;
    private const int MaxParentDepth = 5;

    public Task<PointTextCaptureResult> CaptureAsync(Point screenPoint, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new PointTextCaptureResult(null));
        }

        try
        {
            AutomationElement? element = AutomationElement.FromPoint(new WpfPoint(screenPoint.X, screenPoint.Y));
            if (element is null)
            {
                return Task.FromResult(new PointTextCaptureResult(null));
            }

            PointTextCaptureResult? textPatternResult = TryCaptureTextPattern(element, screenPoint);
            if (textPatternResult?.HasText == true)
            {
                return Task.FromResult(textPatternResult);
            }

            PointTextCaptureResult? elementTextResult = TryCaptureElementText(element, screenPoint);
            if (elementTextResult?.HasText == true)
            {
                return Task.FromResult(elementTextResult);
            }
        }
        catch
        {
        }

        return Task.FromResult(new PointTextCaptureResult(null));
    }

    private static PointTextCaptureResult? TryCaptureTextPattern(AutomationElement element, Point screenPoint)
    {
        foreach (AutomationElement candidate in EnumerateSelfAndParents(element))
        {
            try
            {
                if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject) ||
                    patternObject is not TextPattern textPattern)
                {
                    continue;
                }

                TextPatternRange range = textPattern.RangeFromPoint(new WpfPoint(screenPoint.X, screenPoint.Y));
                range.ExpandToEnclosingUnit(TextUnit.Word);
                string text = TextSanitizer.NormalizeForTranslation(range.GetText(MaxElementTextLength));
                if (!TextSanitizer.IsUsefulForTranslation(text))
                {
                    continue;
                }

                return new PointTextCaptureResult(text, "来自 UI Automation");
            }
            catch
            {
            }
        }

        return null;
    }

    private static PointTextCaptureResult? TryCaptureElementText(AutomationElement element, Point screenPoint)
    {
        foreach (AutomationElement candidate in EnumerateSelfAndParents(element).Take(2))
        {
            try
            {
                Rectangle bounds = ToRectangle(candidate.Current.BoundingRectangle);
                if (bounds.IsEmpty || !bounds.Contains(screenPoint))
                {
                    continue;
                }

                foreach (string text in GetElementTexts(candidate))
                {
                    string normalizedText = TextSanitizer.NormalizeForTranslation(text);
                    if (!TextSanitizer.IsUsefulForTranslation(normalizedText) ||
                        !IsElementTextCandidate(candidate, bounds, normalizedText))
                    {
                        continue;
                    }

                    string selectedText = TextTokenSelector.SelectNearestTokenText(normalizedText, bounds, screenPoint, MaxTokenDistance);
                    if (selectedText == normalizedText && normalizedText.Length > MaxElementTextLength)
                    {
                        continue;
                    }

                    if (TextSanitizer.IsUsefulForTranslation(selectedText))
                    {
                        return new PointTextCaptureResult(selectedText, "来自 UI Automation");
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool IsElementTextCandidate(AutomationElement element, Rectangle bounds, string text)
    {
        ControlType controlType = element.Current.ControlType;
        bool acceptedControlType =
            controlType == ControlType.Text ||
            controlType == ControlType.Edit ||
            controlType == ControlType.Button ||
            controlType == ControlType.Hyperlink ||
            controlType == ControlType.MenuItem ||
            controlType == ControlType.ListItem ||
            controlType == ControlType.TabItem ||
            controlType == ControlType.DataItem;

        if (acceptedControlType)
        {
            return true;
        }

        if (text.Length > 40)
        {
            return false;
        }

        return bounds.Width <= 480 && bounds.Height <= 120;
    }

    private static IEnumerable<AutomationElement> EnumerateSelfAndParents(AutomationElement element)
    {
        AutomationElement? current = element;
        for (int depth = 0; current is not null && depth < MaxParentDepth; depth++)
        {
            yield return current;

            try
            {
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch
            {
                yield break;
            }
        }
    }

    private static IEnumerable<string> GetElementTexts(AutomationElement element)
    {
        string name = element.Current.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            yield return name;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObject) &&
            valuePatternObject is ValuePattern valuePattern &&
            !string.IsNullOrWhiteSpace(valuePattern.Current.Value))
        {
            yield return valuePattern.Current.Value;
        }
    }

    private static Rectangle ToRectangle(WpfRect rect)
    {
        if (rect.IsEmpty ||
            double.IsNaN(rect.Left) ||
            double.IsNaN(rect.Top) ||
            double.IsNaN(rect.Width) ||
            double.IsNaN(rect.Height) ||
            double.IsInfinity(rect.Left) ||
            double.IsInfinity(rect.Top) ||
            double.IsInfinity(rect.Width) ||
            double.IsInfinity(rect.Height))
        {
            return Rectangle.Empty;
        }

        int left = (int)Math.Floor(rect.Left);
        int top = (int)Math.Floor(rect.Top);
        int width = Math.Max(1, (int)Math.Ceiling(rect.Width));
        int height = Math.Max(1, (int)Math.Ceiling(rect.Height));
        return new Rectangle(left, top, width, height);
    }
}
