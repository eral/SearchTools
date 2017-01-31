// (C) 2016 ERAL
// Distributed under the Boost Software License, Version 1.0.
// (See copy at http://www.boost.org/LICENSE_1_0.txt)

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace SearchTools {
	public class RomIncludeViewer : EditorWindow {

		/// <summary>
		/// メニュー化
		/// </summary>
		[MenuItem("Window/Search Tools/Rom Include Viewer")]
		public static void Menu() {
			var st = GetWindow<RomIncludeViewer>("Rom Include Viewer");
			st.Show();
		}

		/// <summary>
		/// 構築
		/// </summary>
		public void OnEnable() {
			EditorApplication.projectWindowItemOnGUI += ProjectWindowItemOnGUI;
			Selection.selectionChanged += Repaint;

			includeIcons = new[]{
				AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SearchTools/Textures/ExcludeIcon.png"),
				AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SearchTools/Textures/IncludeIcon.png"),
				AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SearchTools/Textures/UnknownIcon.png"),
				AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SearchTools/Textures/AmbiguousIcon.png"),
			};

			StartAnalyze();
		}

		/// <summary>
		/// 破棄
		/// </summary>
		public void OnDisable() {
			Selection.selectionChanged -= Repaint;
			EditorApplication.projectWindowItemOnGUI -= ProjectWindowItemOnGUI;

			QuitAnalyze();
			EditorApplication.RepaintProjectWindow();
		}

		/// <summary>
		/// 描画
		/// </summary>
		public void OnGUI() {
			Toolbar();
			if (EditorSettings.serializationMode != SerializationMode.ForceText) {
				EditorGUILayout.HelpBox("\"Editor Settings/Asset Serialization\" isn't \"Force Text\"", MessageType.Warning);
			}
			LinkView();
		}

		/// <summary>
		/// 解析モード
		/// </summary>
		private enum AnalyzeMode {
			InboundLinks,
			Links,
		}

		/// <summary>
		/// EditorGUI.Foldoutの三角形の幅
		/// </summary>
		private const float foldoutWidth = 10.0f;

		/// <summary>
		/// モードラベル
		/// </summary>
		private static readonly GUIContent[] modeLabels = System.Enum.GetNames(typeof(AnalyzeMode)).Select(x=>new GUIContent(x)).ToArray();

		/// <summary>
		/// 解析モード
		/// </summary>
		[SerializeField]
		private AnalyzeMode analyzeMode = AnalyzeMode.InboundLinks;

		/// <summary>
		/// リンクビューステート
		/// </summary>
		private struct LinkViewState {
			public Vector2 scrollPosition;
			public Dictionary<string, bool> Foldouts;

			public static LinkViewState identity {get{
				return new LinkViewState(){scrollPosition = Vector2.zero, Foldouts = new Dictionary<string, bool>()};
			}}
		}

		/// <summary>
		/// リンクビューステート
		/// </summary>
		private LinkViewState[] linkViewStates = Enumerable.Repeat(LinkViewState.identity, System.Enum.GetValues(typeof(AnalyzeMode)).Length).ToArray();

		/// <summary>
		/// 解析器
		/// </summary>
		private LinkAnalyzer linkAnalyzer {get{return linkAnalyzerField ?? (linkAnalyzerField = new LinkAnalyzer());}}
		private LinkAnalyzer linkAnalyzerField = null;

		/// <summary>
		/// 梱包判定アイコン
		/// </summary>
		private static Texture2D[] includeIcons;

#if SEARCH_TOOLS_DEBUG
		/// <summary>
		/// GUID表示
		/// </summary>
		[SerializeField]
		private bool displayGUID = false;
#endif

		/// <summary>
		/// ツールバー
		/// </summary>
		/// <returns></returns>
		private Rect Toolbar() {
			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			analyzeMode = (AnalyzeMode)GUILayout.SelectionGrid((int)analyzeMode, modeLabels, modeLabels.Length, EditorStyles.toolbarButton);
			GUILayout.FlexibleSpace();
#if SEARCH_TOOLS_DEBUG
			displayGUID = GUILayout.Toggle(displayGUID, "GUID", EditorStyles.toolbarButton);
			if (linkAnalyzer.analyzing) {
				if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) {
					Refresh();
				}
			}
#endif
			{
				var progressBarPosition = GUILayoutUtility.GetRect(60.0f, EditorStyles.toolbar.fixedHeight);
				if (linkAnalyzer.analyzing) {
					EditorGUI.ProgressBar(progressBarPosition, linkAnalyzer.progress, "analyzing");
				} else {
					if (GUI.Button(progressBarPosition, "Refresh", EditorStyles.toolbarButton)) {
						Refresh();
					}
				}
			}
			GUILayout.EndHorizontal();

			var toolbarHeight = EditorStyles.toolbar.fixedHeight - EditorStyles.toolbar.border.bottom;
			var result = new Rect(new Vector2(0.0f, toolbarHeight)
								, new Vector2(position.size.x, position.size.y - toolbarHeight)
								);
			return result;
		}

		/// <summary>
		/// リンクビュー
		/// </summary>
		private void LinkView() {
			if (0 < Selection.objects.Length) {
				var sortedObjects = Selection.objects.Select(x=>new{obj = x, sortValue = GetSortStringInLinkView(x)}).ToList();
				sortedObjects.Sort((x,y)=>{
					return string.Compare(x.sortValue, y.sortValue);
				});
				using (var scrollView = new EditorGUILayout.ScrollViewScope(linkViewStates[(int)analyzeMode].scrollPosition)) {
					foreach (var sortedObject in sortedObjects) { 
						linkViewStates[(int)analyzeMode].scrollPosition = scrollView.scrollPosition;

						var uniqueID = LinkAnalyzer.ConvertObjectToUniqueID(sortedObject.obj);
						LinkView(uniqueID, string.Empty);
					}
				}
			}
		}

		/// <summary>
		/// リンクビュー内ソート用文字列取得
		/// </summary>
		private static string GetSortStringInLinkView(Object obj) {
			//フォルダを前方に移動させる為に、フォルダではないオブジェクトを後方と判定させる
			//    その為に、フォルダではないオブジェクトの手前に在るディレクトリ区切り文字を'|'(辞書順で'/'依りも後方に在る)に詐称する
			//    当然ファイルパスとして不正な文字列に為るが、ファイルパスを返す関数では無いので問題無い
			var names = AssetDatabase.GetAssetPath(obj).Split('/');
			var result = names[0];
			var path = names[0];
			foreach (var name in names.Skip(1)) {
				result += (AssetDatabase.IsValidFolder(path + '/' + name)? '/': '|') + name;
				path += '/' + name;
			}
			return result;
		}

		/// <summary>
		/// リンクビュー
		/// </summary>
		private void LinkView(LinkAnalyzer.AssetUniqueID uniqueID, string parentFoldoutUniqueID) {
			var isSpritePackingTag = LinkAnalyzer.IsSpritePackingTag(uniqueID);

			List<LinkAnalyzer.AssetUniqueID> nestLinks;
			if (isSpritePackingTag) { 
				nestLinks = linkAnalyzer.GetLinks(uniqueID);
			} else switch (analyzeMode) {
			case AnalyzeMode.InboundLinks:
				nestLinks = linkAnalyzer.GetInboundLinks(uniqueID);
				break;
			case AnalyzeMode.Links:
			default:
				nestLinks = linkAnalyzer.GetLinks(uniqueID);
				break;
			}
			if ((nestLinks != null) && (nestLinks.Count == 0)) {
				nestLinks = null;
			}
			var childSpritePackingTag = linkAnalyzer.GetSpritePackingTag(uniqueID);
			if (string.IsNullOrEmpty(childSpritePackingTag)) {
				childSpritePackingTag = null;
			}
			var hasChild = (nestLinks != null) || (childSpritePackingTag != null);

			LinkAnalyzer.IncludeStateFlags includeStateFlags = 0;
			if (analyzeMode == AnalyzeMode.InboundLinks) {
				includeStateFlags = linkAnalyzer.GetIncludeStateFlags(uniqueID);
				if ((includeStateFlags != 0) && ((includeStateFlags & LinkAnalyzer.IncludeStateFlags.NonInclude) == 0)) {
					hasChild = true;
				}
			}

			var currentFoldoutUniqueID = parentFoldoutUniqueID + "/" + uniqueID;
			if (!linkViewStates[(int)analyzeMode].Foldouts.ContainsKey(currentFoldoutUniqueID)) {
				linkViewStates[(int)analyzeMode].Foldouts.Add(currentFoldoutUniqueID, false);
			}
			var foldout = linkViewStates[(int)analyzeMode].Foldouts[currentFoldoutUniqueID];

			var position = GUILayoutUtility.GetRect(EditorGUIUtility.fieldWidth, EditorGUIUtility.singleLineHeight);
			if (hasChild) {
				EditorGUI.BeginChangeCheck();
				{
					var indentLevel = EditorGUI.indentLevel;
					var foldoutPosition =EditorGUI.IndentedRect(position);
					foldoutPosition.width = foldoutPosition.height;
					EditorGUI.indentLevel = 0;
					foldout = EditorGUI.Foldout(foldoutPosition, foldout, GUIContent.none);
					EditorGUI.indentLevel = indentLevel;
				}
				if (EditorGUI.EndChangeCheck()) {
					linkViewStates[(int)analyzeMode].Foldouts[currentFoldoutUniqueID] = foldout;
				}
			}
			position.xMin += foldoutWidth;

			if (isSpritePackingTag) {
				//SpritePackingTag
				var currentSpritePackingTag = LinkAnalyzer.ConvertUniqueIDToSpritePackingTag(uniqueID);
				SpritePackingTagsField(position, currentSpritePackingTag);
			} else { 
				//Object
				AssetField(position, uniqueID);
			}

			if (foldout) {
				++EditorGUI.indentLevel;
				if (nestLinks != null) {
					foreach (var nestUniqueID in nestLinks) {
						LinkView(nestUniqueID, currentFoldoutUniqueID);
					}
				}
				if (childSpritePackingTag != null) {
					var nestUniqueID = LinkAnalyzer.ConvertSpritePackingTagToUniqueID(childSpritePackingTag);
					LinkView(nestUniqueID, currentFoldoutUniqueID);
				}

				if ((includeStateFlags & LinkAnalyzer.IncludeStateFlags.Scripts) != 0) {
					IncludeLabelView("Scripts");
				}
				if ((includeStateFlags & LinkAnalyzer.IncludeStateFlags.Resources) != 0) {
					IncludeLabelView("Resources");
				}
				if ((includeStateFlags & LinkAnalyzer.IncludeStateFlags.StreamingAssets) != 0) {
					IncludeLabelView("StreamingAssets");
				}
				if ((includeStateFlags & LinkAnalyzer.IncludeStateFlags.ScenesInBuild) != 0) {
					IncludeLabelView("Build Settings");
				}
				if ((includeStateFlags & LinkAnalyzer.IncludeStateFlags.AlwaysIncludedShaders) != 0) {
					IncludeLabelView("Project Setting/Graphics Settings");
				}

				--EditorGUI.indentLevel;
			}
		}

		/// <summary>
		/// アセットフィールド
		/// </summary>
		private void AssetField(Rect position, LinkAnalyzer.AssetUniqueID uniqueID) {
#if SEARCH_TOOLS_DEBUG
			if (displayGUID) { 
				var label = uniqueID.fileID.ToString("D9") + ":" + uniqueID.guid;
				EditorGUI.LabelField(position, label);
			} else //[fallthrough]
#endif
			CustomGUI.ObjectLabelField(position, uniqueID.guid, uniqueID.fileID);

			var include = linkAnalyzer.IsInclude(uniqueID);
			position.xMin = position.xMax - position.height;
			GUI.DrawTexture(position, includeIcons[(int)include]);
		}

		/// <summary>
		/// スプライトパッキングタグフィールド
		/// </summary>
		private void SpritePackingTagsField(Rect position, string tag) {
			var content = new GUIContent(tag + " (SpritePackingTag)", EditorGUIUtility.FindTexture("PreTextureMipMapHigh"));
			EditorGUI.LabelField(position, content);

			var include = linkAnalyzer.IsIncludeFromSpritePackingTag(tag);
			position.xMin = position.xMax - position.height;
			GUI.DrawTexture(position, includeIcons[(int)include]);
		}

		/// <summary>
		/// 梱包ラベルビュー
		/// </summary>
		private void IncludeLabelView(string label) {
			var position = GUILayoutUtility.GetRect(EditorGUIUtility.fieldWidth, EditorGUIUtility.singleLineHeight);
			position.xMin += foldoutWidth;

			var icon = includeIcons[(int)LinkAnalyzer.IsIncludeReturn.True];
			var content = new GUIContent(label, icon);
			EditorGUI.LabelField(position, content);

			position.xMin = position.xMax - position.height;
			GUI.DrawTexture(position, icon);
		}

		/// <summary>
		/// ProjectWindow描画
		/// </summary>
		/// <param name="guid">GUID</param>
		/// <param name="selectionRect">選択矩形</param>
		private void ProjectWindowItemOnGUI(string guid, Rect selectionRect) {
			var path = AssetDatabase.GUIDToAssetPath(guid);
			var include = IsInclude(path);
			var pos = selectionRect;
			pos.x = pos.xMax - pos.height;
			pos.width = pos.height;
			GUI.DrawTexture(pos, includeIcons[(int)include]);
		}

		/// <summary>
		/// 梱包確認
		/// </summary>
		/// <param name="path">パス</param>
		/// <returns>true:梱包される, false:梱包されない</returns>
		private LinkAnalyzer.IsIncludeReturn IsInclude(string path) {
			return linkAnalyzer.IsIncludeFromPath(path);
		}

		/// <summary>
		/// 解析開始
		/// </summary>
		private void StartAnalyze() {
			linkAnalyzer.Start();
			EditorApplication.update += AnalyzingUpdate;
		}

		/// <summary>
		/// 解析中更新
		/// </summary>
		private void AnalyzingUpdate() {
			Repaint();
			EditorApplication.RepaintProjectWindow();

			if (!linkAnalyzer.analyzing) {
				EditorApplication.update -= AnalyzingUpdate;
			}
		}

		/// <summary>
		/// アプリケーション終了
		/// </summary>
		private void QuitAnalyze() {
			EditorApplication.update -= AnalyzingUpdate;
			linkAnalyzer.Dispose();
		}

		/// <summary>
		/// 再解析
		/// </summary>
		private void Refresh() {
			linkAnalyzer.Refresh();
			EditorApplication.update += AnalyzingUpdate;
		}
	}
}
