// Copyright (c) 2012-2021 Wojciech Figat. All rights reserved.

using System;
using System.Globalization;
using FlaxEngine;
using FlaxEngine.GUI;

namespace FlaxEditor.GUI.Timeline.GUI
{
    /// <summary>
    /// The timeline background control.
    /// </summary>
    /// <seealso cref="FlaxEngine.GUI.ContainerControl" />
    class Background : ContainerControl
    {
        private readonly Timeline _timeline;
        private float[] _tickSteps;
        private float[] _tickStrengths;

        /// <summary>
        /// Initializes a new instance of the <see cref="Background"/> class.
        /// </summary>
        /// <param name="timeline">The timeline.</param>
        public Background(Timeline timeline)
        {
            _timeline = timeline;
            _tickSteps = Utilities.Utils.CurveTickSteps;
            _tickStrengths = new float[_tickSteps.Length];
        }

        /// <inheritdoc />
        public override bool IntersectsContent(ref Vector2 locationParent, out Vector2 location)
        {
            // Pass all events
            location = PointFromParent(ref locationParent);
            return true;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            var style = Style.Current;
            var mediaBackground = _timeline.MediaBackground;
            var tracks = _timeline.Tracks;
            var linesColor = style.BackgroundNormal;
            var areaLeft = -X;
            var areaRight = Parent.Width + mediaBackground.ControlsBounds.BottomRight.X;
            var height = Height;

            // Calculate the timeline range in the view to optimize background drawing
            Render2D.PeekClip(out var globalClipping);
            Render2D.PeekTransform(out var globalTransform);
            var globalRect = new Rectangle(globalTransform.M31 + areaLeft, globalTransform.M32, areaRight * globalTransform.M11, height * globalTransform.M22);
            var globalMask = Rectangle.Shared(globalClipping, globalRect);
            var globalTransformInv = Matrix3x3.Invert(globalTransform);
            var localRect = Rectangle.FromPoints(Matrix3x3.Transform2D(globalMask.UpperLeft, globalTransformInv), Matrix3x3.Transform2D(globalMask.BottomRight, globalTransformInv));
            var localRectMin = localRect.UpperLeft;
            var localRectMax = localRect.BottomRight;

            // Draw lines between tracks
            Render2D.DrawLine(new Vector2(areaLeft, 0.5f), new Vector2(areaRight, 0.5f), linesColor);
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (track.Visible)
                {
                    var top = track.Bottom + 0.5f;
                    Render2D.DrawLine(new Vector2(areaLeft, top), new Vector2(areaRight, top), linesColor);
                }
            }

            // Highlight selected tracks
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (track.Visible && _timeline.SelectedTracks.Contains(track) && _timeline.ContainsFocus)
                {
                    Render2D.FillRectangle(new Rectangle(areaLeft, track.Top, areaRight, track.Height), style.BackgroundSelected);
                }
            }

            // Setup time axis ticks
            var minDistanceBetweenTicks = 50.0f;
            var maxDistanceBetweenTicks = 100.0f;
            var zoom = Timeline.UnitsPerSecond * _timeline.Zoom;
            var left = Vector2.Min(localRectMin, localRectMax).X;
            var right = Vector2.Max(localRectMin, localRectMax).X;
            var leftFrame = Mathf.Floor((left - Timeline.StartOffset) / zoom) * _timeline.FramesPerSecond;
            var rightFrame = Mathf.Ceil((right - Timeline.StartOffset) / zoom) * _timeline.FramesPerSecond;
            var min = leftFrame;
            var max = rightFrame;
            int smallestTick = 0;
            int biggestTick = _tickSteps.Length - 1;
            for (int i = _tickSteps.Length - 1; i >= 0; i--)
            {
                // Calculate how far apart these modulo tick steps are spaced
                float tickSpacing = _tickSteps[i] * _timeline.Zoom;

                // Calculate the strength of the tick markers based on the spacing
                _tickStrengths[i] = Mathf.Saturate((tickSpacing - minDistanceBetweenTicks) / (maxDistanceBetweenTicks - minDistanceBetweenTicks));

                // Beyond threshold the ticks don't get any bigger or fatter
                if (_tickStrengths[i] >= 1)
                    biggestTick = i;

                // Do not show small tick markers
                if (tickSpacing <= minDistanceBetweenTicks)
                {
                    smallestTick = i;
                    break;
                }
            }
            int tickLevels = biggestTick - smallestTick + 1;

            // Draw vertical lines for time axis
            for (int level = 0; level < tickLevels; level++)
            {
                float strength = _tickStrengths[smallestTick + level];
                if (strength <= Mathf.Epsilon)
                    continue;

                // Draw all ticks
                int l = Mathf.Clamp(smallestTick + level, 0, _tickSteps.Length - 1);
                var lStep = _tickSteps[l];
                var lNextStep = _tickSteps[l + 1];
                int startTick = Mathf.FloorToInt(min / lStep);
                int endTick = Mathf.CeilToInt(max / lStep);
                Color lineColor = style.ForegroundDisabled.RGBMultiplied(0.7f).AlphaMultiplied(strength);
                for (int i = startTick; i <= endTick; i++)
                {
                    if (l < biggestTick && (i % Mathf.RoundToInt(lNextStep / lStep) == 0))
                        continue;
                    var tick = i * lStep;
                    var time = tick / _timeline.FramesPerSecond;
                    var x = time * zoom + Timeline.StartOffset;

                    // Draw line
                    Render2D.FillRectangle(new Rectangle(x - 0.5f, 0, 1.0f, height), lineColor);
                }
            }

            DrawChildren();

            // Disabled overlay
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (track.DrawDisabled && track.IsExpandedAll)
                {
                    Render2D.FillRectangle(new Rectangle(areaLeft, track.Top, areaRight, track.Height), new Color(0, 0, 0, 100));
                }
            }

            // Darken area outside the duration
            {
                var outsideDurationAreaColor = new Color(0, 0, 0, 100);
                var leftSideMin = PointFromParent(Vector2.Zero);
                var leftSideMax = BottomLeft;
                var rightSideMin = UpperRight;
                var rightSideMax = PointFromParent(Parent.BottomRight) + mediaBackground.ControlsBounds.BottomRight;
                Render2D.FillRectangle(new Rectangle(leftSideMin, leftSideMax.X - leftSideMin.X, height), outsideDurationAreaColor);
                Render2D.FillRectangle(new Rectangle(rightSideMin, rightSideMax.X - rightSideMin.X, height), outsideDurationAreaColor);
            }

            // Draw time axis header
            var timeAxisHeaderOffset = -_timeline.MediaBackground.ViewOffset.Y;
            var verticalLinesHeaderExtend = Timeline.HeaderTopAreaHeight * 0.5f;
            var timeShowMode = _timeline.TimeShowMode;
            Render2D.FillRectangle(new Rectangle(areaLeft, timeAxisHeaderOffset - Timeline.HeaderTopAreaHeight, areaRight - areaLeft, Timeline.HeaderTopAreaHeight), style.Background.RGBMultiplied(0.7f));
            for (int level = 0; level < tickLevels; level++)
            {
                float strength = _tickStrengths[smallestTick + level];
                if (strength <= Mathf.Epsilon)
                    continue;

                // Draw all ticks
                int l = Mathf.Clamp(smallestTick + level, 0, _tickSteps.Length - 1);
                var lStep = _tickSteps[l];
                var lNextStep = _tickSteps[l + 1];
                int startTick = Mathf.FloorToInt(min / lStep);
                int endTick = Mathf.CeilToInt(max / lStep);
                Color lineColor = style.Foreground.RGBMultiplied(0.8f).AlphaMultiplied(strength);
                Color labelColor = style.ForegroundDisabled.AlphaMultiplied(strength);
                for (int i = startTick; i <= endTick; i++)
                {
                    if (l < biggestTick && (i % Mathf.RoundToInt(lNextStep / lStep) == 0))
                        continue;
                    var tick = i * lStep;
                    var time = tick / _timeline.FramesPerSecond;
                    var x = time * zoom + Timeline.StartOffset;

                    // Header line
                    var lineRect = new Rectangle(x - 0.5f, -verticalLinesHeaderExtend + timeAxisHeaderOffset, 1.0f, verticalLinesHeaderExtend);
                    Render2D.FillRectangle(lineRect, lineColor);

                    // Time label
                    string labelText;
                    switch (timeShowMode)
                    {
                    case Timeline.TimeShowModes.Frames:
                        labelText = tick.ToString("###0", CultureInfo.InvariantCulture);
                        break;
                    case Timeline.TimeShowModes.Seconds:
                        labelText = time.ToString("###0.##'s'", CultureInfo.InvariantCulture);
                        break;
                    case Timeline.TimeShowModes.Time:
                        labelText = TimeSpan.FromSeconds(time).ToString("g");
                        break;
                    default: throw new ArgumentOutOfRangeException();
                    }
                    var labelRect = new Rectangle(x + 2, -verticalLinesHeaderExtend + timeAxisHeaderOffset, 50, verticalLinesHeaderExtend);
                    Render2D.DrawText(style.FontSmall, labelText, labelRect, labelColor, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap, 1.0f, 0.8f);
                }
            }
        }

        /// <inheritdoc />
        public override bool OnMouseWheel(Vector2 location, float delta)
        {
            if (base.OnMouseWheel(location, delta))
                return true;

            // Zoom in/out
            if (IsMouseOver && Root.GetKey(KeyboardKeys.Control))
            {
                var locationTimeOld = _timeline.MediaBackground.PointFromParent(_timeline, _timeline.Size * 0.5f).X;
                var frame = (locationTimeOld - Timeline.StartOffset * 2.0f) / _timeline.Zoom / Timeline.UnitsPerSecond * _timeline.FramesPerSecond;
                
                _timeline.Zoom += delta * 0.1f;

                var locationTimeNew = frame / _timeline.FramesPerSecond * Timeline.UnitsPerSecond * _timeline.Zoom + Timeline.StartOffset * 2.0f;
                var locationTimeDelta = locationTimeNew - locationTimeOld;
                var scroll = _timeline.MediaBackground.HScrollBar;
                if (scroll.Visible && scroll.Enabled)
                    scroll.TargetValue += locationTimeDelta;
                return true;
            }

            // Scroll view horizontally
            if (IsMouseOver && Root.GetKey(KeyboardKeys.Shift))
            {
                var scroll = _timeline.MediaBackground.HScrollBar;
                if (scroll.Visible && scroll.Enabled)
                {
                    scroll.TargetValue -= delta * Timeline.UnitsPerSecond / _timeline.Zoom;
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            // Cleanup
            _tickSteps = null;
            _tickStrengths = null;

            base.OnDestroy();
        }
    }
}
