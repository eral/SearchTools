// (C) 2016 ERAL
// Distributed under the Boost Software License, Version 1.0.
// (See copy at http://www.boost.org/LICENSE_1_0.txt)

using UnityEngine;
using UnityEditor;

namespace SearchTools {
	public static partial class CustomGUI {
		public static float ProgressBar(Rect position, float value) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBar(position, value, style);
		}
		public static float ProgressBar(Rect position, float value, GUIStyle style) {
			return ProgressBar(position, value, new GUIContent(value.ToString("0%")), style);
		}

		public static float ProgressBar(Rect position, GUIContent label, float value) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBar(position, label, value, style);
		}
		public static float ProgressBar(Rect position, GUIContent label, float value, GUIStyle style) {
			return ProgressBar(position, value, label, style);
		}

		public static float ProgressBar(Rect position, string label, float value) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBar(position, label, value, style);
		}
		public static float ProgressBar(Rect position, string label, float value, GUIStyle style) {
			return ProgressBar(position, value, new GUIContent(label), style);
		}

		public static void ProgressBar(Rect position, SerializedProperty property) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBar(position, property, style);
		}
		public static void ProgressBar(Rect position, SerializedProperty property, GUIStyle style) {
			ProgressBar(position, property, GUIContent.none, style);
		}

		public static void ProgressBar(Rect position, SerializedProperty property, GUIContent label) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBar(position, property, label, style);
		}
		public static void ProgressBar(Rect position, SerializedProperty property, GUIContent label, GUIStyle style) {
			EditorGUI.BeginChangeCheck();
			var value = ProgressBar(position, property.floatValue, label, style);
			if (EditorGUI.EndChangeCheck()) {
				property.floatValue = value;
			}

			EditorGUI.EndProperty();
		}

		public static void ProgressBarWithLabel(Rect position, SerializedProperty property, string label) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBarWithLabel(position, property, label, style);
		}
		public static void ProgressBarWithLabel(Rect position, SerializedProperty property, string label, GUIStyle style) {
			ProgressBarWithLabel(position, property, new GUIContent(label), style);
		}

		public static float ProgressBarWithLabel(Rect position, GUIContent label, float value) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBarWithLabel(position, label, value, style);
		}
		public static float ProgressBarWithLabel(Rect position, GUIContent label, float value, GUIStyle style) {
			position = EditorGUI.PrefixLabel(position, label);
			return ProgressBar(position, value, style);
		}

		public static float ProgressBarWithLabel(Rect position, string label, float value) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBarWithLabel(position, label, value, style);
		}
		public static float ProgressBarWithLabel(Rect position, string label, float value, GUIStyle style) {
			return ProgressBarWithLabel(position, new GUIContent(label), value, style);
		}

		public static void ProgressBarWithLabel(Rect position, SerializedProperty property, GUIContent label) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBarWithLabel(position, property, label, style);
		}
		public static void ProgressBarWithLabel(Rect position, SerializedProperty property, GUIContent label, GUIStyle style) {
			label = EditorGUI.BeginProperty(position, label, property);

			EditorGUI.BeginChangeCheck();
			var value = ProgressBar(position, property.floatValue, style);
			if (EditorGUI.EndChangeCheck()) {
				property.floatValue = value;
			}

			EditorGUI.EndProperty();
		}

		private static float ProgressBar(Rect position, float value, GUIContent content, GUIStyle style) {
			int controlID = GUIUtility.GetControlID(FocusType.Passive);

			switch (Event.current.GetTypeForControl(controlID)) {
			case EventType.Repaint:
				{
					var pixelWidth = (int)Mathf.Lerp(0.0f, position.width, value);
					var targetRect = new Rect(position){width = pixelWidth};
					var OldGUIcolor = GUI.color;
					GUI.color = new Color(0.5f, 1.0f, 0.5f, 1.0f);
					GUI.Box(targetRect, content, style);
					GUI.color = OldGUIcolor;
				}
				break;
			case EventType.MouseDown:
				if (position.Contains(Event.current.mousePosition) && (Event.current.button == 0)) {
					GUIUtility.hotControl = controlID;
				}
				break;
 
			case EventType.MouseUp:
				if (GUIUtility.hotControl == controlID) {
					GUIUtility.hotControl = 0;
				}
				break;
			}
			if (Event.current.isMouse && (GUIUtility.hotControl == controlID)) {
				value = Mathf.InverseLerp(position.xMin, position.xMax, Event.current.mousePosition.x);
				GUI.changed = true;
				Event.current.Use();
			}
			return value;
		}
	}

	public static partial class CustomGUILayout {
		public static float ProgressBar(float value, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBar(value, style, options);
		}
		public static float ProgressBar(float value, GUIStyle style, params GUILayoutOption[] options) {
			var position = GUILayoutUtility.GetRect(GUIContent.none, style, options);
			return CustomGUI.ProgressBar(position, value, style);
		}

		public static float ProgressBar(GUIContent label, float value, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBar(label, value, style, options);
		}
		public static float ProgressBar(GUIContent label, float value, GUIStyle style, params GUILayoutOption[] options) {
			var position = GUILayoutUtility.GetRect(label, style, options);
			return CustomGUI.ProgressBar(position, label, value, style);
		}

		public static float ProgressBar(string label, float value, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBar(label, value, style, options);
		}
		public static float ProgressBar(string label, float value, GUIStyle style, params GUILayoutOption[] options) {
			return ProgressBar(new GUIContent(label), value, style, options);
		}

		public static void ProgressBar(SerializedProperty property, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBar(property, style, options);
		}
		public static void ProgressBar(SerializedProperty property, GUIStyle style, params GUILayoutOption[] options) {
			var position = GUILayoutUtility.GetRect(GUIContent.none, style, options);
			CustomGUI.ProgressBar(position, property, style);
		}

		public static void ProgressBar(SerializedProperty property, GUIContent label, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBar(property, label, style, options);
		}
		public static void ProgressBar(SerializedProperty property, GUIContent label, GUIStyle style, params GUILayoutOption[] options) {
			var position = GUILayoutUtility.GetRect(label, style, options);
			CustomGUI.ProgressBar(position, property, label, style);
		}

		public static void ProgressBar(SerializedProperty property, string label, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBar(property, label, style, options);
		}
		public static void ProgressBar(SerializedProperty property, string label, GUIStyle style, params GUILayoutOption[] options) {
			ProgressBar(property, new GUIContent(label), style, options);
		}

		public static float ProgressBarWithLabel(GUIContent label, float value, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBarWithLabel(label, value, style, options);
		}
		public static float ProgressBarWithLabel(GUIContent label, float value, GUIStyle style, params GUILayoutOption[] options) {
			var position = GUILayoutUtility.GetRect(label, style, options);
			return CustomGUI.ProgressBarWithLabel(position, label, value, style);
		}

		public static float ProgressBarWithLabel(string label, float value, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			return ProgressBarWithLabel(label, value, style, options);
		}
		public static float ProgressBarWithLabel(string label, float value, GUIStyle style, params GUILayoutOption[] options) {
			return ProgressBarWithLabel(new GUIContent(label), value, style, options);
		}


		public static void ProgressBarWithLabel(SerializedProperty property, GUIContent label, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBarWithLabel(property, label, style, options);
		}
		public static void ProgressBarWithLabel(SerializedProperty property, GUIContent label, GUIStyle style, params GUILayoutOption[] options) {
			var position = GUILayoutUtility.GetRect(label, style, options);
			CustomGUI.ProgressBarWithLabel(position, property, label, style);
		}

		public static void ProgressBarWithLabel(SerializedProperty property, string label, params GUILayoutOption[] options) {
			var style = CustomGUIDetail.ProgressBarDefaultGUIStyle();
			ProgressBarWithLabel(property, label, style, options);
		}
		public static void ProgressBarWithLabel(SerializedProperty property, string label, GUIStyle style, params GUILayoutOption[] options) {
			ProgressBarWithLabel(property, new GUIContent(label), style, options);
		}
	}

	public static partial class CustomGUIDetail {
		public static GUIStyle ProgressBarDefaultGUIStyle() {
			var result = new GUIStyle(GUI.skin.box);
			result.stretchWidth = true;
			result.padding = new RectOffset();
			return result;
		}
	}
}
