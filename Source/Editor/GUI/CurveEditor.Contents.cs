// Copyright (c) 2012-2021 Wojciech Figat. All rights reserved.

using FlaxEngine;
using FlaxEngine.GUI;

namespace FlaxEditor.GUI
{
    partial class CurveEditor<T>
    {
        /// <summary>
        /// The curve contents container control.
        /// </summary>
        /// <seealso cref="FlaxEngine.GUI.ContainerControl" />
        protected class ContentsBase : ContainerControl
        {
            private readonly CurveEditor<T> _editor;
            internal bool _leftMouseDown;
            private bool _rightMouseDown;
            internal Vector2 _leftMouseDownPos = Vector2.Minimum;
            private Vector2 _rightMouseDownPos = Vector2.Minimum;
            private Vector2 _movingViewLastPos;
            internal Vector2 _mousePos = Vector2.Minimum;
            internal bool _isMovingSelection;
            internal bool _isMovingTangent;
            internal bool _movedKeyframes;
            private TangentPoint _movingTangent;
            private Vector2 _movingSelectionStart;
            private Vector2[] _movingSelectionOffsets;
            private Vector2 _cmShowPos;

            /// <summary>
            /// Initializes a new instance of the <see cref="ContentsBase"/> class.
            /// </summary>
            /// <param name="editor">The curve editor.</param>
            public ContentsBase(CurveEditor<T> editor)
            {
                _editor = editor;
            }

            private void UpdateSelectionRectangle()
            {
                var selectionRect = Rectangle.FromPoints(_leftMouseDownPos, _mousePos);

                // Find controls to select
                for (int i = 0; i < Children.Count; i++)
                {
                    if (Children[i] is KeyframePoint p)
                    {
                        p.IsSelected = p.Bounds.Intersects(ref selectionRect);
                    }
                }

                _editor.UpdateTangents();
            }

            /// <inheritdoc />
            public override bool IntersectsContent(ref Vector2 locationParent, out Vector2 location)
            {
                // Pass all events
                location = PointFromParent(ref locationParent);
                return true;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Vector2 location)
            {
                _mousePos = location;

                base.OnMouseEnter(location);
            }

            /// <inheritdoc />
            public override void OnMouseMove(Vector2 location)
            {
                _mousePos = location;

                // Moving view
                if (_rightMouseDown)
                {
                    Vector2 delta = location - _movingViewLastPos;
                    delta *= GetUseModeMask(_editor.EnablePanning) * _editor.ViewScale;
                    if (delta.LengthSquared > 0.01f)
                    {
                        _editor._mainPanel.ViewOffset += delta;
                        _movingViewLastPos = location;
                        switch (_editor.EnablePanning)
                        {
                        case UseMode.Vertical:
                            Cursor = CursorType.SizeNS;
                            break;
                        case UseMode.Horizontal:
                            Cursor = CursorType.SizeWE;
                            break;
                        case UseMode.On:
                            Cursor = CursorType.SizeAll;
                            break;
                        }
                    }

                    return;
                }
                // Moving selection
                else if (_isMovingSelection)
                {
                    var viewRect = _editor._mainPanel.GetClientArea();
                    var locationKeyframes = PointToKeyframes(location, ref viewRect);
                    var accessor = _editor.Accessor;
                    var components = accessor.GetCurveComponents();
                    for (var i = 0; i < _editor._points.Count; i++)
                    {
                        var p = _editor._points[i];
                        if (p.IsSelected)
                        {
                            var k = _editor.GetKeyframe(p.Index);
                            float time = _editor.GetKeyframeTime(k);
                            float value = _editor.GetKeyframeValue(k, p.Component);

                            float minTime = p.Index != 0 ? _editor.GetKeyframeTime(_editor.GetKeyframe(p.Index - 1)) + Mathf.Epsilon : float.MinValue;
                            float maxTime = p.Index != _editor.KeyframesCount - 1 ? _editor.GetKeyframeTime(_editor.GetKeyframe(p.Index + 1)) - Mathf.Epsilon : float.MaxValue;

                            var offset = _movingSelectionOffsets[i];

                            if (!_editor.ShowCollapsed)
                            {
                                // Move on value axis
                                value = locationKeyframes.Y + offset.Y;
                            }

                            // Let the first selected point of this keyframe to edit time
                            bool isFirstSelected = false;
                            for (var j = 0; j < components; j++)
                            {
                                var idx = p.Index * components + j;
                                if (idx == i)
                                {
                                    isFirstSelected = true;
                                    break;
                                }
                                if (_editor._points[idx].IsSelected)
                                    break;
                            }
                            if (isFirstSelected)
                            {
                                time = locationKeyframes.X + offset.X;

                                if (_editor.FPS.HasValue)
                                {
                                    float fps = _editor.FPS.Value;
                                    time = Mathf.Floor(time * fps) / fps;
                                }
                                time = Mathf.Clamp(time, minTime, maxTime);
                            }

                            // TODO: snapping keyframes to grid when moving

                            _editor.SetKeyframeInternal(p.Index, time, value, p.Component);
                        }
                        _editor.UpdateKeyframes();
                        _editor.UpdateTooltips();
                        if (_editor.EnablePanning == UseMode.On)
                        {
                            //_editor._mainPanel.ScrollViewTo(PointToParent(_editor._mainPanel, location));
                        }
                        Cursor = CursorType.SizeAll;
                        _movedKeyframes = true;
                    }
                    return;
                }
                // Moving tangent
                else if (_isMovingTangent)
                {
                    var viewRect = _editor._mainPanel.GetClientArea();
                    var direction = _movingTangent.IsIn ? -1.0f : 1.0f;
                    var k = _editor.GetKeyframe(_movingTangent.Index);
                    var kv = _editor.GetKeyframeValue(k);
                    var value = _editor.Accessor.GetCurveValue(ref kv, _movingTangent.Component);
                    _movingTangent.TangentValue = direction * (PointToKeyframes(location, ref viewRect).Y - value);
                    _editor.UpdateTangents();
                    Cursor = CursorType.SizeNS;
                    _movedKeyframes = true;
                    return;
                }
                // Selecting
                else if (_leftMouseDown)
                {
                    UpdateSelectionRectangle();
                    return;
                }

                base.OnMouseMove(location);
            }

            /// <inheritdoc />
            public override void OnLostFocus()
            {
                // Clear flags and state
                if (_leftMouseDown)
                {
                    _leftMouseDown = false;
                }
                if (_rightMouseDown)
                {
                    _rightMouseDown = false;
                    Cursor = CursorType.Default;
                }
                _isMovingSelection = false;
                _isMovingTangent = false;

                base.OnLostFocus();
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Vector2 location, MouseButton button)
            {
                if (base.OnMouseDown(location, button))
                {
                    // Clear flags
                    _isMovingSelection = false;
                    _isMovingTangent = false;
                    _rightMouseDown = false;
                    _leftMouseDown = false;
                    return true;
                }

                // Cache data
                _isMovingSelection = false;
                _isMovingTangent = false;
                _mousePos = location;
                if (button == MouseButton.Left)
                {
                    _leftMouseDown = true;
                    _leftMouseDownPos = location;
                }
                if (button == MouseButton.Right)
                {
                    _rightMouseDown = true;
                    _rightMouseDownPos = location;
                    _movingViewLastPos = location;
                }

                // Check if any node is under the mouse
                var underMouse = GetChildAt(location);
                if (underMouse is KeyframePoint keyframe)
                {
                    if (_leftMouseDown)
                    {
                        // Check if user is pressing control
                        if (Root.GetKey(KeyboardKeys.Control))
                        {
                            // Add to selection
                            keyframe.IsSelected = true;
                            _editor.UpdateTangents();
                        }
                        // Check if node isn't selected
                        else if (!keyframe.IsSelected)
                        {
                            // Select node
                            _editor.ClearSelection();
                            keyframe.IsSelected = true;
                            _editor.UpdateTangents();
                        }

                        // Start moving selected nodes
                        StartMouseCapture();
                        _isMovingSelection = true;
                        _movedKeyframes = false;
                        var viewRect = _editor._mainPanel.GetClientArea();
                        _movingSelectionStart = PointToKeyframes(location, ref viewRect);
                        if (_movingSelectionOffsets == null || _movingSelectionOffsets.Length != _editor._points.Count)
                            _movingSelectionOffsets = new Vector2[_editor._points.Count];
                        for (int i = 0; i < _movingSelectionOffsets.Length; i++)
                            _movingSelectionOffsets[i] = _editor._points[i].Point - _movingSelectionStart;
                        _editor.OnEditingStart();
                        Focus();
                        Tooltip?.Hide();
                        return true;
                    }
                }
                else if (underMouse is TangentPoint tangent && tangent.Visible)
                {
                    if (_leftMouseDown)
                    {
                        // Start moving tangent
                        StartMouseCapture();
                        _isMovingTangent = true;
                        _movedKeyframes = false;
                        _movingTangent = tangent;
                        _editor.OnEditingStart();
                        Focus();
                        Tooltip?.Hide();
                        return true;
                    }
                }
                else
                {
                    if (_leftMouseDown)
                    {
                        // Start selecting
                        StartMouseCapture();
                        _editor.ClearSelection();
                        _editor.UpdateTangents();
                        Focus();
                        return true;
                    }
                    if (_rightMouseDown)
                    {
                        // Start navigating
                        StartMouseCapture();
                        Focus();
                        return true;
                    }
                }

                Focus();
                return true;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Vector2 location, MouseButton button)
            {
                _mousePos = location;

                if (_leftMouseDown && button == MouseButton.Left)
                {
                    _leftMouseDown = false;
                    EndMouseCapture();
                    Cursor = CursorType.Default;

                    // Editing tangent
                    if (_isMovingTangent)
                    {
                        if (_movedKeyframes)
                        {
                            _editor.OnEdited();
                            _editor.OnEditingEnd();
                            _editor.UpdateKeyframes();
                        }
                    }
                    // Moving keyframes
                    else if (_isMovingSelection)
                    {
                        if (_movedKeyframes)
                        {
                            _editor.OnEdited();
                            _editor.OnEditingEnd();
                        }
                    }
                    // Selecting
                    else
                    {
                        UpdateSelectionRectangle();
                    }

                    _isMovingSelection = false;
                    _isMovingTangent = false;
                    _movedKeyframes = false;
                }
                if (_rightMouseDown && button == MouseButton.Right)
                {
                    _rightMouseDown = false;
                    EndMouseCapture();
                    Cursor = CursorType.Default;

                    // Check if no move has been made at all
                    if (Vector2.Distance(ref location, ref _rightMouseDownPos) < 3.0f)
                    {
                        var selectionCount = _editor.SelectionCount;
                        var underMouse = GetChildAt(location);
                        if (selectionCount == 0 && underMouse is KeyframePoint point)
                        {
                            // Select node
                            selectionCount = 1;
                            point.IsSelected = true;
                            _editor.UpdateTangents();
                        }

                        var viewRect = _editor._mainPanel.GetClientArea();
                        _cmShowPos = PointToKeyframes(location, ref viewRect);

                        var cm = new ContextMenu.ContextMenu();
                        cm.AddButton("Add keyframe", () => _editor.AddKeyframe(_cmShowPos)).Enabled = _editor.KeyframesCount < _editor.MaxKeyframes;
                        if (selectionCount == 0)
                        {
                        }
                        else if (selectionCount == 1)
                        {
                            cm.AddButton("Edit keyframe", () => _editor.EditKeyframes(this, location));
                            cm.AddButton("Remove keyframe", _editor.RemoveKeyframes);
                        }
                        else
                        {
                            cm.AddButton("Edit keyframes", () => _editor.EditKeyframes(this, location));
                            cm.AddButton("Remove keyframes", _editor.RemoveKeyframes);
                        }
                        cm.AddButton("Edit all keyframes", () => _editor.EditAllKeyframes(this, location));
                        if (_editor.EnableZoom != UseMode.Off || _editor.EnablePanning != UseMode.Off)
                        {
                            cm.AddSeparator();
                            cm.AddButton("Show whole curve", _editor.ShowWholeCurve);
                            cm.AddButton("Reset view", _editor.ResetView);
                        }
                        _editor.OnShowContextMenu(cm, selectionCount);
                        cm.Show(this, location);
                    }
                }

                if (base.OnMouseUp(location, button))
                {
                    // Clear flags
                    _rightMouseDown = false;
                    _leftMouseDown = false;
                    return true;
                }

                return true;
            }

            /// <inheritdoc />
            public override bool OnMouseWheel(Vector2 location, float delta)
            {
                if (base.OnMouseWheel(location, delta))
                    return true;

                // Zoom in/out
                if (_editor.EnableZoom != UseMode.Off && IsMouseOver && !_leftMouseDown && RootWindow.GetKey(KeyboardKeys.Control))
                {
                    // TODO: preserve the view center point for easier zooming
                    _editor.ViewScale += GetUseModeMask(_editor.EnableZoom) * (delta * 0.1f);
                    return true;
                }

                return false;
            }

            /// <inheritdoc />
            protected override void SetScaleInternal(ref Vector2 scale)
            {
                base.SetScaleInternal(ref scale);

                _editor.UpdateKeyframes();
            }

            /// <summary>
            /// Converts the input point from curve editor contents control space into the keyframes time/value coordinates.
            /// </summary>
            /// <param name="point">The point.</param>
            /// <param name="curveContentAreaBounds">The curve contents area bounds.</param>
            /// <returns>The result.</returns>
            private Vector2 PointToKeyframes(Vector2 point, ref Rectangle curveContentAreaBounds)
            {
                // Contents -> Keyframes
                return new Vector2(
                                   (point.X + Location.X) / UnitsPerSecond,
                                   (point.Y + Location.Y - curveContentAreaBounds.Height) / -UnitsPerSecond
                                  );
            }
        }
    }
}
