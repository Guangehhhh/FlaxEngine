// Copyright (c) 2012-2021 Wojciech Figat. All rights reserved.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FlaxEditor.GUI.Timeline.Undo;
using FlaxEngine;
using FlaxEngine.GUI;

namespace FlaxEditor.GUI.Timeline.Tracks
{
    /// <summary>
    /// The timeline track for animating object property via Curve.
    /// </summary>
    /// <seealso cref="MemberTrack" />
    public abstract class CurvePropertyTrackBase : MemberTrack
    {
        private sealed class Splitter : Control
        {
            private bool _clicked;
            internal CurvePropertyTrackBase _track;

            public override void Draw()
            {
                var style = Style.Current;
                if (IsMouseOver || _clicked)
                    Render2D.FillRectangle(new Rectangle(Vector2.Zero, Size), _clicked ? style.BackgroundSelected : style.BackgroundHighlighted);
            }

            public override void OnEndMouseCapture()
            {
                base.OnEndMouseCapture();

                _clicked = false;
            }

            public override void Defocus()
            {
                base.Defocus();

                _clicked = false;
            }

            public override void OnMouseEnter(Vector2 location)
            {
                base.OnMouseEnter(location);

                Cursor = CursorType.SizeNS;
            }

            public override void OnMouseLeave()
            {
                Cursor = CursorType.Default;

                base.OnMouseLeave();
            }

            public override bool OnMouseDown(Vector2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _clicked = true;
                    Focus();
                    StartMouseCapture();
                    return true;
                }

                return base.OnMouseDown(location, button);
            }
            
            public override void OnMouseMove(Vector2 location)
            {
                base.OnMouseMove(location);

                if (_clicked)
                {
                    var height = Mathf.Clamp(PointToParent(location).Y, 40.0f, 1000.0f);
                    if (!Mathf.NearEqual(height, _track._expandedHeight))
                    {
                        _track.Height = _track._expandedHeight = height;
                        _track.Timeline.ArrangeTracks();
                    }
                }
            }
            
            public override bool OnMouseUp(Vector2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _clicked)
                {
                    _clicked = false;
                    EndMouseCapture();
                    return true;
                }

                return base.OnMouseUp(location, button);
            }
        }

        private byte[] _curveEditingStartData;
        private float _expandedHeight = 120.0f;
        private Splitter _splitter;

        /// <summary>
        /// The curve editor.
        /// </summary>
        public CurveEditorBase Curve;

        private const float CollapsedHeight = 20.0f;

        /// <inheritdoc />
        public CurvePropertyTrackBase(ref TrackCreateOptions options)
        : base(ref options)
        {
            Height = CollapsedHeight;

            _addKey.Clicked += OnAddKeyClicked;
            _leftKey.Clicked += OnLeftKeyClicked;
            _rightKey.Clicked += OnRightKeyClicked;
        }

        private void OnRightKeyClicked(Image image, MouseButton button)
        {
            if (button == MouseButton.Left && GetNextKeyframeFrame(Timeline.CurrentTime, out var frame))
            {
                Timeline.OnSeek(frame);
            }
        }

        /// <inheritdoc />
        protected override void OnContextMenu(ContextMenu.ContextMenu menu)
        {
            base.OnContextMenu(menu);

            if (Curve == null)
                return;
            menu.AddSeparator();
            menu.AddButton("Copy Preview Value", () =>
            {
                Curve.Evaluate(out var value, Timeline.CurrentTime);
                Clipboard.Text = FlaxEngine.Json.JsonSerializer.Serialize(value);
            }).LinkTooltip("Copies the current track value to the clipboard").Enabled = Timeline.ShowPreviewValues;
        }

        /// <inheritdoc />
        public override bool GetNextKeyframeFrame(float time, out int result)
        {
            if (Curve != null)
            {
                for (int i = 0; i < Curve.KeyframesCount; i++)
                {
                    Curve.GetKeyframe(i, out var kTime, out _, out _, out _);
                    if (kTime > time)
                    {
                        result = Mathf.FloorToInt(kTime * Timeline.FramesPerSecond);
                        return true;
                    }
                }
            }
            return base.GetNextKeyframeFrame(time, out result);
        }

        private void OnAddKeyClicked(Image image, MouseButton button)
        {
            if (button == MouseButton.Left && Curve != null)
            {
                // Evaluate a value
                var time = Timeline.CurrentTime;
                if (!TryGetValue(out var value))
                    Curve.Evaluate(out value, time);

                // Find keyframe at the current location
                for (int i = Curve.KeyframesCount - 1; i >= 0; i--)
                {
                    Curve.GetKeyframe(i, out var kTime, out var kValue, out _, out _);
                    var frame = Mathf.FloorToInt(kTime * Timeline.FramesPerSecond);
                    if (frame == Timeline.CurrentFrame)
                    {
                        // Skip if value is the same
                        if (kValue == value)
                            return;

                        // Update existing key value
                        Curve.SetKeyframeValue(i, value);
                        UpdatePreviewValue();
                        return;
                    }
                }

                // Add a new key
                using (new TrackUndoBlock(this))
                    Curve.AddKeyframe(time, value);
            }
        }

        private void OnLeftKeyClicked(Image image, MouseButton button)
        {
            if (button == MouseButton.Left && GetPreviousKeyframeFrame(Timeline.CurrentTime, out var frame))
            {
                Timeline.OnSeek(frame);
            }
        }

        /// <inheritdoc />
        public override bool GetPreviousKeyframeFrame(float time, out int result)
        {
            if (Curve != null)
            {
                for (int i = Curve.KeyframesCount - 1; i >= 0; i--)
                {
                    Curve.GetKeyframe(i, out var kTime, out _, out _, out _);
                    if (kTime < time)
                    {
                        result = Mathf.FloorToInt(kTime * Timeline.FramesPerSecond);
                        return true;
                    }
                }
            }
            return base.GetPreviousKeyframeFrame(time, out result);
        }

        private void UpdateCurve()
        {
            if (Curve == null || Timeline == null)
                return;
            Curve.Visible = Visible;
            if (!Visible)
                return;
            var expanded = IsExpanded;
            Curve.Bounds = new Rectangle(Timeline.StartOffset, Y + 1.0f, Timeline.Duration * Timeline.UnitsPerSecond * Timeline.Zoom, Height - 2.0f);
            Curve.ViewScale = new Vector2(Timeline.Zoom, Curve.ViewScale.Y);
            Curve.ShowCollapsed = !expanded;
            Curve.ShowBackground = expanded;
            Curve.ShowAxes = expanded;
            Curve.EnableZoom = expanded ? CurveEditorBase.UseMode.Vertical : CurveEditorBase.UseMode.Off;
            Curve.EnablePanning = expanded ? CurveEditorBase.UseMode.Vertical : CurveEditorBase.UseMode.Off;
            Curve.ScrollBars = expanded ? ScrollBars.Vertical : ScrollBars.None;
            Curve.UpdateKeyframes();
            if (expanded)
            {
                if(_splitter == null)
                    _splitter = new Splitter
                    {
                        _track = this,
                        Parent = Curve,
                    };
                var splitterHeight = 4.0f;
                _splitter.Bounds = new Rectangle(0, Curve.Height - splitterHeight, Curve.Width, splitterHeight);
            }
        }

        private void OnKeyframesEdited()
        {
            UpdatePreviewValue();
            Timeline.MarkAsEdited();
        }

        private void OnCurveEditingStart()
        {
            _curveEditingStartData = EditTrackAction.CaptureData(this);
        }

        private void OnCurveEditingEnd()
        {
            var after = EditTrackAction.CaptureData(this);
            if (!Utils.ArraysEqual(_curveEditingStartData, after))
                Timeline.Undo.AddAction(new EditTrackAction(Timeline, this, _curveEditingStartData, after));
            _curveEditingStartData = null;
        }

        private void UpdatePreviewValue()
        {
            if (Curve == null)
            {
                _previewValue.Text = string.Empty;
                return;
            }

            var time = Timeline.CurrentTime;
            Curve.Evaluate(out var value, time);
            _previewValue.Text = GetValueText(value);
        }

        /// <summary>
        /// Creates the curve.
        /// </summary>
        /// <param name="propertyType">Type of the property (keyframes value).</param>
        /// <param name="curveEditorType">Type of the curve editor (generic type of the curve editor).</param>
        protected void CreateCurve(Type propertyType, Type curveEditorType)
        {
            curveEditorType = curveEditorType.MakeGenericType(propertyType);
            Curve = (CurveEditorBase)Activator.CreateInstance(curveEditorType);
            Curve.EnableZoom = CurveEditorBase.UseMode.Vertical;
            Curve.EnablePanning = CurveEditorBase.UseMode.Vertical;
            Curve.ScrollBars = ScrollBars.Vertical;
            Curve.Parent = Timeline?.MediaPanel;
            Curve.FPS = Timeline?.FramesPerSecond;
            Curve.Edited += OnKeyframesEdited;
            Curve.EditingStart += OnCurveEditingStart;
            Curve.EditingEnd += OnCurveEditingEnd;
            if (Timeline != null)
            {
                Curve.UnlockChildrenRecursive();
                UpdateCurve();
            }
        }

        private void DisposeCurve()
        {
            if (Curve == null)
                return;
            Curve.Edited -= OnKeyframesEdited;
            Curve.Dispose();
            Curve = null;
            _splitter = null;
        }

        /// <inheritdoc />
        public override object Evaluate(float time)
        {
            if (Curve != null)
            {
                Curve.Evaluate(out var result, time);
                return result;
            }

            return base.Evaluate(time);
        }

        /// <inheritdoc />
        protected override bool CanExpand => true;

        /// <inheritdoc />
        protected override void OnMemberChanged(MemberInfo value, Type type)
        {
            base.OnMemberChanged(value, type);

            DisposeCurve();
        }

        /// <inheritdoc />
        protected override void OnVisibleChanged()
        {
            base.OnVisibleChanged();

            UpdateCurve();
        }

        /// <inheritdoc />
        protected override void OnExpandedChanged()
        {
            Height = IsExpanded ? _expandedHeight : CollapsedHeight;
            UpdateCurve();
            if (IsExpanded)
                Curve.ShowWholeCurve();

            base.OnExpandedChanged();
        }

        /// <inheritdoc />
        public override void OnTimelineChanged(Timeline timeline)
        {
            base.OnTimelineChanged(timeline);

            if (Curve != null)
            {
                Curve.Parent = timeline?.MediaPanel;
                Curve.FPS = timeline?.FramesPerSecond;
                UpdateCurve();
            }
            UpdatePreviewValue();
        }

        /// <inheritdoc />
        public override void OnUndo()
        {
            base.OnUndo();

            UpdatePreviewValue();
        }

        /// <inheritdoc />
        public override void OnTimelineZoomChanged()
        {
            base.OnTimelineZoomChanged();

            UpdateCurve();
        }

        /// <inheritdoc />
        public override void OnTimelineArrange()
        {
            base.OnTimelineArrange();

            UpdateCurve();
        }

        /// <inheritdoc />
        public override void OnTimelineFpsChanged(float before, float after)
        {
            base.OnTimelineFpsChanged(before, after);

            if (Curve != null)
            {
                Curve.FPS = after;
            }
            UpdatePreviewValue();
        }

        /// <inheritdoc />
        public override void OnTimelineCurrentFrameChanged(int frame)
        {
            base.OnTimelineCurrentFrameChanged(frame);

            UpdatePreviewValue();
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            DisposeCurve();

            base.OnDestroy();
        }
    }

    /// <summary>
    /// The timeline track for animating object property via Bezier Curve.
    /// </summary>
    /// <seealso cref="MemberTrack" />
    /// <seealso cref="CurvePropertyTrackBase" />
    public sealed class CurvePropertyTrack : CurvePropertyTrackBase
    {
        /// <summary>
        /// Gets the archetype.
        /// </summary>
        /// <returns>The archetype.</returns>
        public static TrackArchetype GetArchetype()
        {
            return new TrackArchetype
            {
                TypeId = 10,
                Name = "Property",
                DisableSpawnViaGUI = true,
                Create = options => new CurvePropertyTrack(ref options),
                Load = LoadTrack,
                Save = SaveTrack,
            };
        }

        private static void LoadTrack(int version, Track track, BinaryReader stream)
        {
            var e = (CurvePropertyTrack)track;

            e.ValueSize = stream.ReadInt32();
            int propertyNameLength = stream.ReadInt32();
            int propertyTypeNameLength = stream.ReadInt32();
            int keyframesCount = stream.ReadInt32();

            var propertyName = stream.ReadBytes(propertyNameLength);
            e.MemberName = Encoding.UTF8.GetString(propertyName, 0, propertyNameLength);
            if (stream.ReadChar() != 0)
                throw new Exception("Invalid track data.");

            var propertyTypeName = stream.ReadBytes(propertyTypeNameLength);
            e.MemberTypeName = Encoding.UTF8.GetString(propertyTypeName, 0, propertyTypeNameLength);
            if (stream.ReadChar() != 0)
                throw new Exception("Invalid track data.");

            var keyframes = new object[keyframesCount];
            var dataBuffer = new byte[e.ValueSize];
            var propertyType = Scripting.TypeUtils.GetType(e.MemberTypeName).Type;
            if (propertyType == null)
            {
                stream.ReadBytes(keyframesCount * (sizeof(float) + e.ValueSize * 3));
                if (!string.IsNullOrEmpty(e.MemberTypeName))
                    Editor.LogError("Cannot load track " + e.MemberName + " of type " + e.MemberTypeName + ". Failed to find the value type information.");
                return;
            }

            GCHandle handle = GCHandle.Alloc(dataBuffer, GCHandleType.Pinned);
            for (int i = 0; i < keyframesCount; i++)
            {
                var time = stream.ReadSingle();

                stream.Read(dataBuffer, 0, e.ValueSize);
                var value = Marshal.PtrToStructure(handle.AddrOfPinnedObject(), propertyType);

                stream.Read(dataBuffer, 0, e.ValueSize);
                var tangentIn = Marshal.PtrToStructure(handle.AddrOfPinnedObject(), propertyType);

                stream.Read(dataBuffer, 0, e.ValueSize);
                var tangentOut = Marshal.PtrToStructure(handle.AddrOfPinnedObject(), propertyType);

                keyframes[i] = new BezierCurve<object>.Keyframe
                {
                    Time = time,
                    Value = value,
                    TangentIn = tangentIn,
                    TangentOut = tangentOut,
                };
            }
            handle.Free();

            if (e.Curve != null && e.Curve.ValueType != propertyType)
            {
                e.Curve.Dispose();
                e.Curve = null;
            }
            if (e.Curve == null)
            {
                e.CreateCurve(propertyType, typeof(BezierCurveEditor<>));
            }
            e.Curve.SetKeyframes(keyframes);
        }

        private static void SaveTrack(Track track, BinaryWriter stream)
        {
            var e = (CurvePropertyTrack)track;

            var propertyName = e.MemberName ?? string.Empty;
            var propertyNameData = Encoding.UTF8.GetBytes(propertyName);
            if (propertyNameData.Length != propertyName.Length)
                throw new Exception(string.Format("The object member name bytes data has different size as UTF8 bytes. Type {0}.", propertyName));

            var propertyTypeName = e.MemberTypeName ?? string.Empty;
            var propertyTypeNameData = Encoding.UTF8.GetBytes(propertyTypeName);
            if (propertyTypeNameData.Length != propertyTypeName.Length)
                throw new Exception(string.Format("The object member typename bytes data has different size as UTF8 bytes. Type {0}.", propertyTypeName));

            var keyframesCount = e.Curve?.KeyframesCount ?? 0;

            stream.Write(e.ValueSize);
            stream.Write(propertyNameData.Length);
            stream.Write(propertyTypeNameData.Length);
            stream.Write(keyframesCount);

            stream.Write(propertyNameData);
            stream.Write('\0');

            stream.Write(propertyTypeNameData);
            stream.Write('\0');

            if (keyframesCount == 0)
                return;
            var dataBuffer = new byte[e.ValueSize];
            IntPtr ptr = Marshal.AllocHGlobal(e.ValueSize);
            for (int i = 0; i < keyframesCount; i++)
            {
                e.Curve.GetKeyframe(i, out var time, out var value, out var tangentIn, out var tangentOut);
                stream.Write(time);

                Marshal.StructureToPtr(value, ptr, true);
                Marshal.Copy(ptr, dataBuffer, 0, e.ValueSize);
                stream.Write(dataBuffer);

                Marshal.StructureToPtr(tangentIn, ptr, true);
                Marshal.Copy(ptr, dataBuffer, 0, e.ValueSize);
                stream.Write(dataBuffer);

                Marshal.StructureToPtr(tangentOut, ptr, true);
                Marshal.Copy(ptr, dataBuffer, 0, e.ValueSize);
                stream.Write(dataBuffer);
            }
            Marshal.FreeHGlobal(ptr);
        }

        /// <inheritdoc />
        public CurvePropertyTrack(ref TrackCreateOptions options)
        : base(ref options)
        {
        }

        /// <inheritdoc />
        protected override void OnMemberChanged(MemberInfo value, Type type)
        {
            base.OnMemberChanged(value, type);

            if (type != null)
            {
                CreateCurve(type, typeof(BezierCurveEditor<>));
            }
        }
    }
}
