// (C) 2016 ERAL
// Distributed under the Boost Software License, Version 1.0.
// (See copy at http://www.boost.org/LICENSE_1_0.txt)

using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Text;

namespace SearchTools {
	public class LinkAnalyzer : System.IDisposable {

		public struct AssetUniqueID {
			public string guid;
			public int fileID;

			public AssetUniqueID(string guid, int fileID) {
				this.guid = guid;
				this.fileID = fileID;
			}
			public AssetUniqueID(string guid) : this(guid, 0) {
			}

			public static AssetUniqueID none {get{
				return new AssetUniqueID(string.Empty, 0);
			}}

			public override bool Equals(object other) {
				if (other is AssetUniqueID) {
					var p = (AssetUniqueID)other;
					return (guid == p.guid) && (fileID == p.fileID);
				}
				return false;
			}

			public override int GetHashCode() {
				var fileIDHashCode = fileID.GetHashCode();
				var result = (fileIDHashCode << 17) ^ (int)((uint)fileIDHashCode >> 15);
				result ^= ((guid != null)? guid.GetHashCode(): unchecked((int)0xF0F0F0F0));
				return  result;
			}

			public override string ToString() {
				string result;
				if (fileID == 0) {
					result = guid;
				} else {
					var sb = new StringBuilder(44);
					sb.Append(guid);
					sb.Append(":");
					sb.Append(fileID);
					result = sb.ToString();
				}
				return result;
			}
		}

		/// <summary>
		/// IsInclude戻り値
		/// </summary>
		public enum IsIncludeReturn {
			False,
			True,
			Unknown,
			Ambiguous,
		}

		/// <summary>
		/// 梱包情報
		/// </summary>
		[System.Flags]
		public enum IncludeStateFlags { 
			NonInclude				= 1<<0,
			AssetBundle				= 1<<1,
			Link					= 1<<2,
			Scripts					= 1<<3,
			Resources				= 1<<4,
			StreamingAssets			= 1<<5,
			ScenesInBuild			= 1<<6,
			AlwaysIncludedShaders	= 1<<7,
		}

		/// <summary>
		/// 解析中確認
		/// </summary>
		public  bool analyzing {get{
			return (analyzeOnMainThreadUpdate != null) || (analyzeThread != null);
		}}

		/// <summary>
		/// 解析スレッド
		/// </summary>
		public  float progress {get{
			return ((analyzing)? Mathf.Min(analyzeProgress, 1.0f - float.Epsilon): analyzeProgress);
		}}

		/// <summary>
		/// IDisposableインターフェース
		/// </summary>
		public void Dispose() {
			if(analyzeOnMainThreadUpdate != null) {
				analyzeOnMainThreadUpdate = null;
			}
			if(analyzeThread != null) {
#if !SEARCH_TOOLS_DEBUG
				analyzeThread.Abort();
#endif
				analyzeThread = null;
			}
		}

		/// <summary>
		/// オブジェクトをユニークIDに変換
		/// </summary>
		public static AssetUniqueID ConvertObjectToUniqueID(Object obj) {
			var assetPath = AssetDatabase.GetAssetPath(obj);
			var guid = AssetDatabase.AssetPathToGUID(assetPath);
			var result = new AssetUniqueID(guid);

			bool hasFileID;
			var linkerType = GetLinkerType(assetPath);
			switch (linkerType) {
			case GetLinkerTypeReturn.Home:
			case GetLinkerTypeReturn.MetaHome:
			case GetLinkerTypeReturn.Script:
				hasFileID = false;
				break;
			case GetLinkerTypeReturn.Importer:
				hasFileID = AssetDatabase.IsSubAsset(obj);
				break;
			default:
				hasFileID = true;
				break;
			}
			if (hasFileID) {
				var instanceID = obj.GetInstanceID();
				var fileID = Unsupported.GetLocalIdentifierInFile(instanceID);
				result.fileID = fileID;
			}
			return result;
		}

		/// <summary>
		/// ユニークIDをオブジェクトに変換
		/// </summary>
		public static Object ConvertUniqueIDToObject(AssetUniqueID uniqueID) {
			Object result = null;
			if (IsAsset(uniqueID)) {
				var assetPath = AssetDatabase.GUIDToAssetPath(uniqueID.guid);
				if (uniqueID.fileID != 0) {
					result = CustomGUIDetail.LoadAllAssetsAtPath(assetPath)
										.Where(x=>Unsupported.GetLocalIdentifierInFile(x.GetInstanceID()) == uniqueID.fileID)
										.FirstOrDefault();
				} else {
					result = AssetDatabase.LoadMainAssetAtPath(assetPath);
				}
			}
			return result;
		}

		/// <summary>
		/// アセットユニークID確認
		/// </summary>
		public static bool IsAsset(AssetUniqueID uniqueID) {
			var result = false;
			if (!string.IsNullOrEmpty(uniqueID.guid)) {
				var match = guidMatchRegex.Match("guid: " + uniqueID.guid);
				result = match.Success;
			}
			return result;
		}

		/// <summary>
		/// スプライトパッキングユニークID確認
		/// </summary>
		public static bool IsSpritePackingTag(AssetUniqueID uniqueID) {
			var result = false;
			if (!string.IsNullOrEmpty(uniqueID.guid)) {
				result = uniqueID.guid.StartsWith(spritePackingTagsPrefix);
				result = result && (uniqueID.fileID == 0);
			}
			return result;
		}

		/// <summary>
		/// アセットバンドルユニークID確認
		/// </summary>
		public static bool IsAssetBundle(AssetUniqueID uniqueID) {
			var result = false;
			if (!string.IsNullOrEmpty(uniqueID.guid)) {
				result = uniqueID.guid.StartsWith(assetBundlesPrefix);
				result = result && (uniqueID.fileID == 0);
			}
			return result;
		}

		/// <summary>
		/// ユニークIDの梱包確認
		/// </summary>
		public IsIncludeReturn IsInclude(AssetUniqueID uniqueID) {
			var result = IsIncludeReturn.Unknown;
			if (analyzeData.ContainsKey(uniqueID)) {
				var state = analyzeData[uniqueID].state;
				if (state != 0) {
					state &= ~(IncludeStateFlags.NonInclude | IncludeStateFlags.AssetBundle);
					result = (((state != 0))? IsIncludeReturn.True: IsIncludeReturn.False);
				}
			}
			return result;
		}

		/// <summary>
		/// パスの梱包確認
		/// </summary>
		public IsIncludeReturn IsIncludeFromPath(string path) {
			var result = IsIncludeReturn.Unknown;
			if (pathToGuid.ContainsKey(path)) {
				var guid = pathToGuid[path];
				if (includeGuid.ContainsKey(guid)) {
					result = includeGuid[guid];
				}
			}
			return result;
		}

		/// <summary>
		/// ユニークIDの梱包情報取得
		/// </summary>
		public IncludeStateFlags GetIncludeStateFlags(AssetUniqueID uniqueID) {
			IncludeStateFlags result = 0;
			if (analyzeData.ContainsKey(uniqueID)) {
				result = analyzeData[uniqueID].state;
			}
			return result;
		}

		/// <summary>
		/// パスの梱包情報取得
		/// </summary>
		public IncludeStateFlags GetIncludeStateFlags(string path) {
			IncludeStateFlags result = 0;
			if (pathToGuid.ContainsKey(path)) {
				var guid = pathToGuid[path];
				var uniqueID = new AssetUniqueID(guid);
				result = GetIncludeStateFlags(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// スプライトパッキングタグの梱包確認
		/// </summary>
		public IsIncludeReturn IsIncludeFromSpritePackingTag(string tag) {
			return IsInclude(ConvertSpritePackingTagToUniqueID(tag));
		}

		/// <summary>
		/// リンク取得
		/// </summary>
		public List<AssetUniqueID> GetLinks(AssetUniqueID uniqueID) {
			List<AssetUniqueID> result = null;
			if (analyzeData.ContainsKey(uniqueID) && (analyzeData[uniqueID].state != 0)) {
				result = analyzeData[uniqueID].links;
			} else if (uniqueID.fileID != 0) {
				uniqueID.fileID = 0;
				result = GetLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// リンク取得
		/// </summary>
		public List<AssetUniqueID> GetLinks(string path) {
			List<AssetUniqueID> result = null;
			if (pathToGuid.ContainsKey(path)) {
				var guid = pathToGuid[path];
				var uniqueID = new AssetUniqueID(guid);
				result = GetLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// 逆リンク取得
		/// </summary>
		public List<AssetUniqueID> GetInboundLinks(AssetUniqueID uniqueID) {
			List<AssetUniqueID> result = null;
			if (analyzeData.ContainsKey(uniqueID) && (analyzeData[uniqueID].state != 0)) {
				result = analyzeData[uniqueID].inboundLinks;
			} else if (uniqueID.fileID != 0) {
				uniqueID.fileID = 0;
				result = GetInboundLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// 逆リンク取得
		/// </summary>
		public List<AssetUniqueID> GetInboundLinks(string path) {
			List<AssetUniqueID> result = null;
			if (pathToGuid.ContainsKey(path)) {
				var guid = pathToGuid[path];
				var uniqueID = new AssetUniqueID(guid);
				result = GetInboundLinks(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// パスからスプライトパッキングタグ取得
		/// </summary>
		public string GetSpritePackingTag(AssetUniqueID uniqueID) {
			string result = null;
			if (analyzeData.ContainsKey(uniqueID) && (analyzeData[uniqueID].state != 0)) {
				result = analyzeData[uniqueID].spritePackingTag;
			} else if (uniqueID.fileID != 0) {
				uniqueID.fileID = 0;
				result = GetSpritePackingTag(uniqueID);
			}
			return result;
		}

		/// <summary>
		/// パスからスプライトパッキングタグ取得
		/// </summary>
		public string GetSpritePackingTag(string path) {
			return GetSpritePackingTag(ConvertSpritePackingTagToUniqueID(path));
		}

		/// <summary>
		/// SpritePackingTagをユニークIDに変換
		/// </summary>
		public static AssetUniqueID ConvertSpritePackingTagToUniqueID(string spritePackingTag) {
			return new AssetUniqueID(spritePackingTagsPrefix + spritePackingTag, 0);
		}

		/// <summary>
		/// ユニークIDをSpritePackingTagに変換
		/// </summary>
		public static string ConvertUniqueIDToSpritePackingTag(AssetUniqueID uniqueID) {
			string result = null;
			if (IsSpritePackingTag(uniqueID)) { 
				result = uniqueID.guid.Substring(spritePackingTagsPrefix.Length);
			}
			return result;
		}

		/// <summary>
		/// AssetBundleをユニークIDに変換
		/// </summary>
		public static AssetUniqueID ConvertAssetBundleToUniqueID(string assetBundle) {
			return new AssetUniqueID(assetBundlesPrefix + assetBundle, 0);
		}

		/// <summary>
		/// ユニークIDをAssetBundleに変換
		/// </summary>
		public static string ConvertUniqueIDToAssetBundle(AssetUniqueID uniqueID) {
			string result = null;
			if (IsAssetBundle(uniqueID)) { 
				result = uniqueID.guid.Substring(assetBundlesPrefix.Length);
			}
			return result;
		}

		/// <summary>
		/// 開始
		/// </summary>
		public void Start() {
			if (analyzeProgress == 0.0f) {
				AnalyzeStart();
			}
		}

		/// <summary>
		/// リフレッシュ
		/// </summary>
		public void Refresh() {
			if(analyzeOnMainThreadUpdate != null) {
				analyzeOnMainThreadUpdate = null;
			}
			if(analyzeThread != null) {
#if !SEARCH_TOOLS_DEBUG
				analyzeThread.Abort();
#endif
				analyzeThread = null;
			}
			analyzeProgress = 0.0f;
			Start();
		}

		/// <summary>
		/// メインスレッド解析のフレーム毎稼動上限秒
		/// </summary>
		private const float mainThreadUpdateTimeLimitSecond = 0.016f;

		/// <summary>
		/// メインスレッド解析用コルーチン
		/// </summary>
		private IEnumerator<object> analyzeOnMainThreadUpdate;

		/// <summary>
		/// 解析スレッド
		/// </summary>
#if SEARCH_TOOLS_DEBUG
		private int? analyzeThread = null;
#else
		private Thread analyzeThread = null;
#endif

		/// <summary>
		/// 解析進捗の区切り(～GUID辞書生成)
		/// </summary>
		private const float analyzeProgressAnalyzeOnMainThreadGuids = 0.01f;

		/// <summary>
		/// 解析進捗の区切り(～リンク構築)
		/// </summary>
		private const float analyzeProgressAnalyzeMain = 0.94f;

		/// <summary>
		/// 解析進捗の区切り(～FileID正規化)
		/// </summary>
		private const float analyzeProgressFileIDNormalize = 0.95f;

		/// <summary>
		/// 解析進捗の区切り(～逆リンク構築)
		/// </summary>
		private const float analyzeProgressInboundLink = 1.00f;

		/// <summary>
		/// 解析進捗の区切り(～アセットバンドルラベル辞書生成)
		/// </summary>
		private const float analyzeProgressAnalyzeOnMainThreadAssetBundles = 0.90f;

		/// <summary>
		/// 解析進捗の区切り(～アセットバンドルフラグ付与)
		/// </summary>
		private const float analyzeProgressAddAssetBundleFlag = 1.00f;

		/// <summary>
		/// 解析進捗
		/// </summary>
		private float analyzeProgress = 0.0f;

		/// <summary>
		/// 解析進捗範囲
		/// </summary>
		private Vector2 analyzeProgressRange = new Vector2(0.0f, 1.0f);

		/// <summary>
		/// 解析進捗カウント
		/// </summary>
		private float analyzeProgressDelta = 0.0f;

		/// <summary>
		/// 解析進捗カウント
		/// </summary>
		private float analyzeProgressCount = 0.0f;

		/// <summary>
		/// 解析パス
		/// </summary>
		private static readonly string dataBasePath = Application.dataPath.Substring(0, Application.dataPath.Length - 6); //末端の"Assets"を削除

		/// <summary>
		/// サブアセットマッチング正規表現
		/// </summary>
		private static readonly Regex subAssetMatchRegex = new Regex(@"--- !u![1-9][0-9]* &([1-9][0-9]*)");

		/// <summary>
		/// GUIDマッチング正規表現
		/// </summary>
		private static readonly Regex guidMatchRegex = new Regex(@"guid:[\s&&[^\r\n]]*([0-9a-zA-Z]{32})");

		/// <summary>
		/// fileIDマッチング正規表現
		/// </summary>
		private static readonly Regex fileIDMatchRegex = new Regex(@"fileID:[\s&&[^\r\n]]*([1-9][0-9]*)");

		/// <summary>
		/// SpritePackingTagマッチング正規表現
		/// </summary>
		private static readonly Regex spritePackingTagMatchRegex = new Regex(@"spritePackingTag:[\s&&[^\r\n]]*(.+)");

		/// <summary>
		/// fileIDToRecycleNameルートマッチング正規表現
		/// </summary>
		private static readonly Regex fileIDToRecycleNameRootMatchRegex = new Regex(@"fileIDToRecycleName:");

		/// <summary>
		/// fileIDToRecycleNameノードマッチング正規表現
		/// </summary>
		private static readonly Regex fileIDToRecycleNameNodeMatchRegex = new Regex(@"([1-9][0-9]*):[\s&&[^\r\n]]*.+");

		/// <summary>
		/// リンク情報
		/// </summary>
		private struct LinkInfo {
			public List<AssetUniqueID> links;
			public string spritePackingTag;
		}

		/// <summary>
		/// アセット情報
		/// </summary>
		private class AssetInfo {
			public IncludeStateFlags state;
			public LinkInfo linkInfo;
			public List<AssetUniqueID> links {get{return linkInfo.links;} set{linkInfo.links = value;}}
			public List<AssetUniqueID> inboundLinks;
			public string spritePackingTag {get{return linkInfo.spritePackingTag;} set{linkInfo.spritePackingTag = value;}}

			public AssetInfo() {
				state = 0;
				linkInfo = new LinkInfo(){links = null, spritePackingTag = null};
				inboundLinks = null;
			}
			public AssetInfo(IncludeStateFlags state, List<AssetUniqueID> links, string spritePackingTag) {
				this.state = state;
				linkInfo = new LinkInfo(){links = links, spritePackingTag = spritePackingTag};
				inboundLinks = null;
			}
		}

		/// <summary>
		/// アセットパスのプレフィックス
		/// </summary>
		private const string assetsPrefix = "Assets/";

		/// <summary>
		/// SpritePackingTagsパスのプレフィックス
		/// </summary>
		private const string spritePackingTagsPrefix = "SpritePackingTags/";

		/// <summary>
		/// AssetBundleパスのプレフィックス
		/// </summary>
		private const string assetBundlesPrefix = "AssetBundles/";

		/// <summary>
		/// 解析結果
		/// </summary>
		private Dictionary<AssetUniqueID, AssetInfo> analyzeData = null;

		/// <summary>
		/// GUIDパス梱包結果
		/// </summary>
		private Dictionary<string, IsIncludeReturn> includeGuid = null;

		/// <summary>
		/// 梱包シーンパス
		/// </summary>
		private string[] includeScenePaths = null;

		/// <summary>
		/// GUIDパス変換辞書
		/// </summary>
		private Dictionary<string, string> guidToPath = null;

		/// <summary>
		/// パスメインユニークID変換辞書
		/// </summary>
		private Dictionary<string, string> pathToGuid = null;

		/// <summary>
		/// アセットバンドル梱包バス辞書
		/// </summary>
		private Dictionary<string, string[]> assetPathsFromAssetBundle = null;

		/// <summary>
		/// アセットバンドルリンク辞書
		/// </summary>
		private Dictionary<string, string[]> assetBundleLinks = null;

		/// <summary>
		/// 解析進捗設定
		/// </summary>
		private void ResetAnalyzeProgress() {
			analyzeProgress = 0.0f;
			analyzeProgressRange = Vector2.zero;
			analyzeProgressDelta = 0.0f;
			analyzeProgressCount = 0.0f;
		}

		/// <summary>
		/// 解析進捗範囲設定
		/// </summary>
		private void SetAnalyzeProgressRange(float max, int count) {
			analyzeProgressRange.x = analyzeProgressRange.y;
			analyzeProgressRange.y = max;
			if (analyzeProgress < analyzeProgressRange.x) {
				analyzeProgress = analyzeProgressRange.x;
			}
			if (0 < count) {
				analyzeProgressDelta = 1.0f / count;
			} else {
				analyzeProgressDelta = 0.0f;
				analyzeProgress = analyzeProgressRange.y;
			}
			analyzeProgressCount = 0.0f;
		}

		/// <summary>
		/// 解析進捗設定
		/// </summary>
		private void IncrementAnalyzeProgress() {
			analyzeProgress = Mathf.Lerp(analyzeProgressRange.x, analyzeProgressRange.y, ++analyzeProgressCount * analyzeProgressDelta);
		}

		/// <summary>
		/// 解析開始
		/// </summary>
		private void AnalyzeStart() {
			ResetAnalyzeProgress();

			includeScenePaths = null;
			if (analyzeData == null) {
				analyzeData = new Dictionary<AssetUniqueID, AssetInfo>();
			} else {
				analyzeData.Clear();
			}
			if (includeGuid == null) {
				includeGuid = new Dictionary<string, IsIncludeReturn>();
			} else {
				includeGuid.Clear();
			}
			if (guidToPath == null) {
				guidToPath = new Dictionary<string, string>();
			} else {
				guidToPath.Clear();
			}
			if (pathToGuid == null) {
				pathToGuid = new Dictionary<string, string>();
			} else {
				pathToGuid.Clear();
			}

			if (assetPathsFromAssetBundle == null) {
				assetPathsFromAssetBundle = new Dictionary<string, string[]>();
			} else {
				assetPathsFromAssetBundle.Clear();
			}
			if (assetBundleLinks == null) {
				assetBundleLinks = new Dictionary<string, string[]>();
			} else {
				assetBundleLinks.Clear();
			}

			analyzeOnMainThreadUpdate = AnalyzeOnMainThread();
			EditorApplication.update += AnalyzeOnMainThreadUpdate;
		}

		/// <summary>
		/// メインスレッド解析用更新
		/// </summary>
		private void AnalyzeOnMainThreadUpdate() {
			var startTime = Time.realtimeSinceStartup;

			do {
				if (analyzeOnMainThreadUpdate == null) {
					EditorApplication.update -= AnalyzeOnMainThreadUpdate;
					break;
				} else if (!analyzeOnMainThreadUpdate.MoveNext()) {
					analyzeOnMainThreadUpdate = null;
					EditorApplication.update -= AnalyzeOnMainThreadUpdate;
					break;
				} else {
				}
			} while ((Time.realtimeSinceStartup - startTime) < mainThreadUpdateTimeLimitSecond);
		}

		/// <summary>
		/// メインスレッド解析
		/// </summary>
		private IEnumerator<object> AnalyzeOnMainThread() {
			SetAnalyzeProgressRange(float.Epsilon, 0);

			var allAssetPaths = AssetDatabase.GetAllAssetPaths();
			yield return null;

			includeScenePaths = EditorBuildSettings.scenes.Where(x=>x.enabled)
													.Select(x=>x.path)
													.ToArray();
			if (includeScenePaths.Length == 0) {
				yield return null;
				var activeScenePath = EditorSceneManager.GetActiveScene().path;
				if (!string.IsNullOrEmpty(activeScenePath)) {
					includeScenePaths = new[]{activeScenePath};
				}
			}
			yield return null;

			SetAnalyzeProgressRange(analyzeProgressAnalyzeOnMainThreadGuids, allAssetPaths.Length);
			foreach (var path in allAssetPaths) {
				if (path.StartsWith(assetsPrefix)) {
					var guid = AssetDatabase.AssetPathToGUID(path);
					guidToPath.Add(guid, path);
					pathToGuid.Add(path, guid);

					yield return null;
				}
				IncrementAnalyzeProgress();
			}

#if SEARCH_TOOLS_DEBUG
			analyzeThread = 0;
			Analyze();
#else
			analyzeThread = new Thread(Analyze);
			analyzeThread.Start();
#endif

			yield break;
		}

		/// <summary>
		/// 解析
		/// </summary>
		private void Analyze() {
			SetAnalyzeProgressRange(analyzeProgressAnalyzeMain, pathToGuid.Count);

			//スクリプト梱包判定
			AnalyzeForScriptsAndStreamingAssets();

			//アセット梱包判定
			AnalyzeForAsset();

			//残りを除外判定
			ExcludeForLeftovers();

			//fileIDの正規化
			FileIDNormalize();

			//逆リンク判定
			AnalyzeForInboundLink();

			analyzeOnMainThreadUpdate = AnalyzeAssetBundleOnMainThread();
			EditorApplication.update += AnalyzeOnMainThreadUpdate;
			analyzeThread = null;
		}


		/// <summary>
		/// メインスレッドアセットバンドル解析
		/// </summary>
		private IEnumerator<object> AnalyzeAssetBundleOnMainThread() {
			var allAssetBundleNames = AssetDatabase.GetAllAssetBundleNames();
			if (0 < allAssetBundleNames.Length) {
				ResetAnalyzeProgress();
				SetAnalyzeProgressRange(analyzeProgressAnalyzeOnMainThreadAssetBundles, allAssetBundleNames.Length);

				foreach (var assetBundleName in allAssetBundleNames) {
					assetPathsFromAssetBundle.Add(assetBundleName, AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName));
					assetBundleLinks.Add(assetBundleName, AssetDatabase.GetAssetBundleDependencies(assetBundleName, false));
					IncrementAnalyzeProgress();
					yield return null;
				}

#if SEARCH_TOOLS_DEBUG
				analyzeThread = 0;
				AnalyzeAssetBundle();
#else
				analyzeThread = new Thread(AnalyzeAssetBundle);
				analyzeThread.Start();
#endif
			}
			yield break;
		}

		/// <summary>
		/// アセットバンドル解析
		/// </summary>
		private void AnalyzeAssetBundle() {
			//アセットバンドル属性付与
			AddAssetBundleFlag();

			analyzeThread = null;
		}

		/// <summary>
		/// スクリプトとStreamingAssetsの梱包判定
		/// </summary>
		private void AnalyzeForScriptsAndStreamingAssets() {
			foreach (var ptg in pathToGuid) {
				var path = ptg.Key;

				var linkerType = GetLinkerType(path);
				switch (linkerType) {
				case GetLinkerTypeReturn.Script:
					{
						var guid = ptg.Value;
						var dat = new AssetInfo();
						analyzeData.Add(new AssetUniqueID(guid), dat);

						if (path.StartsWith("Assets/StreamingAssets/")) {
							dat.state = IncludeStateFlags.StreamingAssets;
						} else if (-1 == path.IndexOf("/Editor/")) {
							dat.state = IncludeStateFlags.Scripts;
						} else {
							dat.state = IncludeStateFlags.NonInclude;
						}

						includeGuid.Add(guid,  ConvertIncludeStateFlagsToIsIncludeReturn(dat.state));
						IncrementAnalyzeProgress();
					}
					break;
				default:
					{
						if (path.StartsWith("Assets/StreamingAssets/")) {
							var guid = ptg.Value;
							var dat = new AssetInfo(IncludeStateFlags.StreamingAssets, null, null);
							analyzeData.Add(new AssetUniqueID(guid), dat);

							includeGuid.Add(guid,  ConvertIncludeStateFlagsToIsIncludeReturn(dat.state));
							IncrementAnalyzeProgress();
						}
					}
					break;
				}
			}
		}

		/// <summary>
		/// アセット梱包判定
		/// </summary>
		private void AnalyzeForAsset() {
			var analyzeQueue = new Queue<AssetUniqueID>();

			//信頼されたルートの検索
			var trustedRootPaths = GetTrustedRootPaths();
			foreach (var trustedRootPath in trustedRootPaths.Keys) {
				if (pathToGuid.ContainsKey(trustedRootPath)) {
					var trustedRootUniqueID = new AssetUniqueID(pathToGuid[trustedRootPath]);
					analyzeQueue.Enqueue(trustedRootUniqueID);
				}
			}

			while (0 < analyzeQueue.Count) {
				var analyzeUniqueID = analyzeQueue.Dequeue();
				var analyzePath = guidToPath[analyzeUniqueID.guid];

				if (includeGuid.ContainsKey(analyzeUniqueID.guid) && !analyzeData.ContainsKey(analyzeUniqueID)) {
					analyzeUniqueID.fileID = 0;
				}

				var nonIncludeCount = 0;
				if (!analyzeData.ContainsKey(analyzeUniqueID) || (analyzeData[analyzeUniqueID].state == 0)) {
					var linkInfos = GetLinkUniqueIDsFromAssetPath(analyzePath);
					if (!linkInfos.ContainsKey(analyzeUniqueID)) {
						analyzeUniqueID.fileID = 0;
					}
					foreach (var linkInfo in linkInfos) {
						if (!analyzeData.ContainsKey(linkInfo.Key)) {
							analyzeData.Add(linkInfo.Key, new AssetInfo());
						}
						var dat = analyzeData[linkInfo.Key];
						dat.linkInfo = linkInfo.Value;

						if (trustedRootPaths.ContainsKey(analyzePath)) {
							//信頼されたルートなら
							//信頼されたルートの梱包判定に従う
							dat.state = trustedRootPaths[analyzePath];
						} else {
							//信頼されたルートでないなら
							if (linkInfo.Key.fileID == analyzeUniqueID.fileID) {
								//自アセットなら
								//他アセットからリンクされている
								dat.state = IncludeStateFlags.Link;
							} else {
								//相席アセットなら
								//取り敢えず除外判定
								dat.state = IncludeStateFlags.NonInclude;
								++nonIncludeCount;
							}
						}

						if ((dat.links != null)) {
							//リンクしたアセットがあるなら
							foreach (var link in dat.links) {
								if (link.guid == analyzeUniqueID.guid) {
									//相席アセットなら
									if ((dat.state != IncludeStateFlags.NonInclude) && linkInfos.ContainsKey(link)) {
										//自アセットが梱包対象であり、対象アセットが存在するなら
										//解析キューに積む(対象アセットが除外判定されていた場合に梱包対象に変更する為)
										analyzeQueue.Enqueue(link);
									}
								} else {
									//他アセットなら
									if (!includeGuid.ContainsKey(link.guid)) {
										//まだ未解析なら
										//解析キューに積む
										analyzeQueue.Enqueue(link);
									}
								}
							}
						}

						if (!string.IsNullOrEmpty(dat.spritePackingTag)) {
							//スプライトパッキング対象なら
							//スプライトパッキングとの相互リンクを構築する
							var spritePackingTagUniqueID = ConvertSpritePackingTagToUniqueID(dat.spritePackingTag);
							if (!analyzeData.ContainsKey(spritePackingTagUniqueID)) {
								analyzeData.Add(spritePackingTagUniqueID, new AssetInfo(IncludeStateFlags.Link, new List<AssetUniqueID>(), null));
							}
							analyzeData[spritePackingTagUniqueID].links.Add(analyzeUniqueID);
						}
					}

					if (!includeGuid.ContainsKey(analyzeUniqueID.guid)) {
						includeGuid.Add(analyzeUniqueID.guid, ((0 < nonIncludeCount)? IsIncludeReturn.Ambiguous: IsIncludeReturn.True));
					} else if ((nonIncludeCount == 0) && (includeGuid[analyzeUniqueID.guid] == IsIncludeReturn.Ambiguous)) {
						includeGuid[analyzeUniqueID.guid] = IsIncludeReturn.True;
					}

					IncrementAnalyzeProgress();
				} else if (analyzeData[analyzeUniqueID].state == IncludeStateFlags.NonInclude) {
					analyzeData[analyzeUniqueID].state = IncludeStateFlags.Link;
					if (analyzeData[analyzeUniqueID].links != null) {
						foreach (var d in analyzeData[analyzeUniqueID].links) {
							if (analyzeData.ContainsKey(d) && (analyzeData[d].state == IncludeStateFlags.NonInclude)) {
								analyzeQueue.Enqueue(d);
							}
						}
					}
					
					if (!analyzeData.Where(x=>x.Key.guid == analyzeUniqueID.guid).Any(x=>x.Value.state == IncludeStateFlags.NonInclude)) {
						includeGuid[analyzeUniqueID.guid] = IsIncludeReturn.True;
					}
				}
			}
		}

		/// <summary>
		/// 信頼されたルートパスを取得
		/// </summary>
		private Dictionary<string, IncludeStateFlags> GetTrustedRootPaths() {
			var result = new Dictionary<string, IncludeStateFlags>();

			//Scenes In Build
			foreach (var includeScenePath in includeScenePaths) {
				if (result.ContainsKey(includeScenePath)) { 
					result[includeScenePath] |= IncludeStateFlags.ScenesInBuild;
				} else { 
					result.Add(includeScenePath, IncludeStateFlags.ScenesInBuild);
				}
			}

			//Always Included Shaders
			foreach (var uniqueID in GetLinkUniqueIDsFromAssetPath("ProjectSettings/GraphicsSettings.asset").Values.Where(x=>x.links != null).SelectMany(x=>x.links)) {
				if (guidToPath.ContainsKey(uniqueID.guid)) {
					var path = guidToPath[uniqueID.guid];
					if (result.ContainsKey(path)) { 
						result[path] |= IncludeStateFlags.AlwaysIncludedShaders;
					} else { 
						result.Add(path, IncludeStateFlags.AlwaysIncludedShaders);
					}
				}
			}

			//Resources
			foreach (var path in pathToGuid.Keys) {
				if (0 <= path.IndexOf("/Resources/")) {
					if (result.ContainsKey(path)) { 
						result[path] |= IncludeStateFlags.Resources;
					} else { 
						result.Add(path, IncludeStateFlags.Resources);
					}
				}
			}

			return result;
		}

		/// <summary>
		/// アセットからリンクパスを取得
		/// </summary>
		private Dictionary<AssetUniqueID, LinkInfo> GetLinkUniqueIDsFromAssetPath(string path) {
			var result = new Dictionary<AssetUniqueID, LinkInfo>();

			List<string> text;
			var linkerType = GetLinkerType(path);
			switch (linkerType) {
			case GetLinkerTypeReturn.Home:
			case GetLinkerTypeReturn.Apartment:
				text = readTextFromAsset(path);
				break;
			case GetLinkerTypeReturn.MetaHome:
			case GetLinkerTypeReturn.MetaApartment:
			case GetLinkerTypeReturn.Importer:
				text = readTextFromAsset(path + ".meta");
				break;
			case GetLinkerTypeReturn.Unknown:
			default:
				text = null;
				break;
			}
			if ((text == null) || (text.Count == 0)) {
				return result;
			}

			var linkInfo = new LinkInfo();
			var uniqueID = new AssetUniqueID();
			if (pathToGuid.ContainsKey(path)) {
				uniqueID.guid = pathToGuid[path];
			}
			foreach (var line in text) {
				//SubAsset確認
				switch (linkerType) {
				case GetLinkerTypeReturn.Home:
				case GetLinkerTypeReturn.MetaHome:
				case GetLinkerTypeReturn.Importer:
					//empty.
					break;
				default:
					{
						var subAssetMatch = subAssetMatchRegex.Match(line);
						int fileID;
						if (subAssetMatch.Success && int.TryParse(subAssetMatch.Groups[1].Value, out fileID)) {
							if (uniqueID.fileID != fileID) {
								//サブアセット区切りなら
								//解析分を現サブアセットに反映
								result.Add(uniqueID, linkInfo);
								//次サブアセットを新規作成
								uniqueID.fileID = fileID;
								linkInfo.links = null;
								linkInfo.spritePackingTag = null;
							}
						}
					}
					break;
				}

				//リンクGUID列挙
				do {
					var fileIDMatch = fileIDMatchRegex.Match(line);
					var guidMatch = guidMatchRegex.Match(line);
					if (fileIDMatch.Success) {
						int fileID = 0;
						int.TryParse(fileIDMatch.Groups[1].Value, out fileID);

						string guid;
						if (guidMatch.Success) {
							guid = guidMatch.Groups[1].Value;
						} else { 
							guid = uniqueID.guid;
						}
						if (!guidToPath.ContainsKey(guid)) {
							break;
						}

						if ((uniqueID.guid != guid) || (uniqueID.fileID != fileID)) {
							if (linkInfo.links == null) {
								linkInfo.links = new List<AssetUniqueID>();
							}
							var linkUniqueID = new AssetUniqueID(guid, fileID);
							linkInfo.links.Add(linkUniqueID);
						}
					}
				} while(false);

				//SpritePackingTag取得
				if (linkerType == GetLinkerTypeReturn.Importer) {
					var match = spritePackingTagMatchRegex.Match(line);
					if (match.Success) {
						var spritePackingTag = match.Groups[1].Value;
						if (spritePackingTag[0] == '\'') {
							spritePackingTag = spritePackingTag.Substring(1, spritePackingTag.Length - 2).Replace("''", "'");
						}
						linkInfo.spritePackingTag = spritePackingTag;
					}
				}
			}
			//Importer系LinkerのfileID解析
			if (linkerType == GetLinkerTypeReturn.Importer) {
				var i = 0;
				var iMax = text.Count;
				while (i < iMax) {
					var line = text[i++];
					var match = fileIDToRecycleNameRootMatchRegex.Match(line);
					if (match.Success) {
						break;
					}
				}
				while (i < iMax) {
					var line = text[i++];
					var match = fileIDToRecycleNameNodeMatchRegex.Match(line);
					if (!match.Success) {
						break;
					}
					int fileID;
					if (int.TryParse(match.Groups[1].Value, out fileID)) {
						var nodeUniqueId = new AssetUniqueID(uniqueID.guid, fileID);
						var nodeLinkInfo = new LinkInfo(){links = new List<AssetUniqueID>(){uniqueID}}; //Importer系なら無条件で親を参照
						result.Add(nodeUniqueId, nodeLinkInfo);
					}
				}
			}
			result.Add(uniqueID, linkInfo);

			//重複リンク削除
			foreach (var key in result.Keys) {
				var links = result[key].links;
				if (links != null) {
					links.Sort((x,y)=>{
						var compare = string.Compare(x.guid, y.guid);
						if (compare == 0) {
							compare = x.fileID - y.fileID;
						}
						return compare;
					});
					for (var i = links.Count - 2; 0 <= i; --i) {
						if ((links[i].guid == links[i + 1].guid) && (links[i].fileID == links[i + 1].fileID)) {
							links.RemoveAt(i + 1);
						}
					}
				}
			}

			return result;
		}

		/// <summary>
		/// 梱包情報を梱包判定に変換
		/// </summary>
		private static IsIncludeReturn ConvertIncludeStateFlagsToIsIncludeReturn(IncludeStateFlags flags) {
			var result = IsIncludeReturn.Unknown;
			if (flags == 0) {
				//empty.
			} else if (flags == IncludeStateFlags.NonInclude) {
				result = IsIncludeReturn.False;
			} else {
				result = IsIncludeReturn.True;
			}
			return result;
		}

		/// <summary>
		/// GetLinkerType戻り値
		/// </summary>
		private enum GetLinkerTypeReturn {
			Unknown,
			Home,
			MetaHome,
			Apartment,
			MetaApartment,
			Importer,
			Script,
		}

		/// <summary>
		/// リンカー確認
		/// </summary>
		private static GetLinkerTypeReturn GetLinkerType(string path) {
			var result = GetLinkerTypeReturn.Unknown;

			if (path.StartsWith("Assets/StreamingAssets/")) {
				result = GetLinkerTypeReturn.MetaHome;
			} else {
				var extStartIndex = path.LastIndexOf('.');
				if (extStartIndex < 0) {
					extStartIndex = path.Length;
				} else {
					++extStartIndex;
				}
				var ext = path.Substring(extStartIndex, path.Length - extStartIndex);
				switch (ext) {
				case "prefab":
				case "unity":
					result = GetLinkerTypeReturn.Home;
					break;
				case "anim":
				case "asset":
				case "colors":
				case "controller":
				case "cubemap":
				case "curves":
				case "curvesnormalized":
				case "flare":
				case "gradients":
				case "guiskin":
				case "hdr":
				case "mask":
				case "mat":
				case "materiali":
				case "mixer":
				case "overrideController":
				case "particlecurves":
				case "particlecurvessigned":
				case "particledoublecurves":
				case "particledoublecurvessigned":
				case "prefs":
					result = GetLinkerTypeReturn.Apartment;
					break;
				case "dfont":
				case "fnt":
				case "fon":
				case "fontsettings":
				case "otf":
				case "ttf":
					result = GetLinkerTypeReturn.Apartment;
					break;
				case "giparams":
				case "physicMaterial":
				case "physicsMaterial2D":
				case "renderTexture":
					result = GetLinkerTypeReturn.Home;
					break;
				case "bytes":
				case "cginc":
				case "csv":
				case "htm": case ".html":
				case "json":
				case "shader":
				case "txt":
				case "xml":
				case "yaml":
					result = GetLinkerTypeReturn.MetaHome;
					break;
				case "3df":
				case "3dm":
				case "3dmf":
				case "3ds":
				case "3dv":
				case "3dx":
				case "blend":
				case "c4d":
				case "fbx":
				case "lwo":
				case "lws":
				case "ma":
				case "max":
				case "mb":
				case "mesh":
				case "obj":
				case "vrl":
				case "wrl":
				case "wrz":
					result = GetLinkerTypeReturn.Importer;
					break;
				case "ai":
				case "apng":
				case "bmp":
				case "cdr":
				case "dib":
				case "eps":
				case "exif":
				case "exr":
				case "gif":
				case "ico":
				case "icon":
				case "iff":
				case "j":
				case "j2c":
				case "j2k":
				case "jas":
				case "jiff":
				case "jng":
				case "jp2":
				case "jpc":
				case "jpf":
				case "jpg": case ".jpeg": case ".jpe":
				case "jpw":
				case "jpx":
				case "jtf":
				case "mac":
				case "omf":
				case "pic": case ".pict":
				case "png":
				case "psd":
				case "qif":
				case "qti":
				case "qtif":
				case "tex":
				case "tfw":
				case "tga":
				case "tif": case ".tiff":
				case "wmf":
					result = GetLinkerTypeReturn.Importer;
					break;
				case "aac":
				case "aif":
				case "aiff":
				case "au":
				case "it":
				case "mid":
				case "midi":
				case "mod":
				case "mp3":
				case "mpa":
				case "ogg":
				case "ra":
				case "ram":
				case "s3m":
				case "wav": case ".wave":
				case "wma":
				case "xm":
					result = GetLinkerTypeReturn.Importer;
					break;
				case "asf":
				case "asx":
				case "avi":
				case "dat":
				case "divx":
				case "dvx":
				case "m2l":
				case "m2t":
				case "m2ts":
				case "m2v":
				case "m4e":
				case "m4v":
				case "mjp":
				case "mlv":
				case "mov":
				case "movie":
				case "mp21":
				case "mp4":
				case "mpg": case ".mpeg": case ".mpe":
				case "mpv2":
				case "ogm":
				case "qt":
				case "rm":
				case "rmvb":
				case "wmw":
				case "xvid":
					result = GetLinkerTypeReturn.Importer;
					break;
				case "cs":
				case "js":
					result = GetLinkerTypeReturn.Script;
					break;
				default:
					if (IsDirectory(path)) {
						result = GetLinkerTypeReturn.MetaHome;
					} else {
#if SEARCH_TOOLS_DEBUG
						Debug.Log("Unknown linker type:" + ext + ":" + path);
#endif
						result = GetLinkerTypeReturn.MetaApartment;
					}
					break;
				}
			}
			return result;
		}

		/// <summary>
		/// ディレクトリ確認
		/// </summary>
		private static bool IsDirectory(string path) {
			var fullPath = dataBasePath + path;
			return Directory.Exists(fullPath);
		}

		/// <summary>
		/// アセットからのテキスト読み込み
		/// </summary>
		private List<string> readTextFromAsset(string path) {
			var result = new List<string>();
			try {
				using (var sr = new StreamReader(dataBasePath + path)) {
					while (!sr.EndOfStream) {
						result.Add(sr.ReadLine());
					}
					sr.Close();
				}
			} catch (System.UnauthorizedAccessException) {
				//empty.
			} catch (FileNotFoundException) {
				//empty.
			} catch (System.Exception e) {
				Debug.Log(e);
			}
			return result;
		}

		/// <summary>
		/// LoadAllAssetsAtPath()の使用可能確認
		/// </summary>
		private static bool CanUseLoadAllAssetsAtPath(string path) {
			return !path.EndsWith(".unity");
		}

		/// <summary>
		/// 残りを除外判定
		/// </summary>
		private void ExcludeForLeftovers() {
			foreach(var analyzePath in pathToGuid.Keys) {
				var analyzeUniqueID = new AssetUniqueID(pathToGuid[analyzePath]);

				var includeCount = 0;
				if (!analyzeData.ContainsKey(analyzeUniqueID) || analyzeData[analyzeUniqueID].state == 0) {
					var linkInfos = GetLinkUniqueIDsFromAssetPath(analyzePath);
					foreach (var linkInfo in linkInfos) {
						if (!analyzeData.ContainsKey(linkInfo.Key)) {
							analyzeData.Add(linkInfo.Key, new AssetInfo());
						}
						var dat = analyzeData[linkInfo.Key];
						IncludeStateFlags datState = 0;
						dat.linkInfo = linkInfo.Value;
						if (!string.IsNullOrEmpty(dat.spritePackingTag)) {
							var spritePackingTagUniqueID = ConvertSpritePackingTagToUniqueID(dat.spritePackingTag);
							if (!analyzeData.ContainsKey(spritePackingTagUniqueID)) {
								analyzeData.Add(spritePackingTagUniqueID, new AssetInfo(0, new List<AssetUniqueID>(), null));
							}
							analyzeData[spritePackingTagUniqueID].links.Add(analyzeUniqueID);

							datState = analyzeData[spritePackingTagUniqueID].state;
						}
						if (datState == 0) { 
							datState = IncludeStateFlags.NonInclude;
						} else {
							++includeCount;
						}
						dat.state = datState;
					}
					var includeGuidState = IsIncludeReturn.False;
					if (0 < includeCount) {
						includeGuidState = ((linkInfos.Count == includeCount)? IsIncludeReturn.True: IsIncludeReturn.Ambiguous);
					}
					includeGuid.Add(analyzeUniqueID.guid, includeGuidState);

					IncrementAnalyzeProgress();
				}
			}
		}

		/// <summary>
		/// fileIDの正規化
		/// </summary>
		private void FileIDNormalize() {
			SetAnalyzeProgressRange(analyzeProgressFileIDNormalize, analyzeData.Count);

			foreach(var dat in analyzeData) {
				var assetInfo = dat.Value;
				if (assetInfo.links != null) {
					var uniqueID = dat.Key;
					var links = assetInfo.links;
					for(int i = 0, iMax = links.Count; i < iMax; ++i) {
						if ((links[i].fileID != 0) && !analyzeData.ContainsKey(links[i])) {
							var normalizedLink = new AssetUniqueID(links[i].guid);
							if ((uniqueID.guid == normalizedLink.guid) && (uniqueID.fileID == normalizedLink.fileID)) {
								links.RemoveAt(i);
								--i;
								--iMax;
								continue;
							} else if (analyzeData.ContainsKey(normalizedLink)) {
								links[i] = normalizedLink;
							}
							if (0 < i) {
								if ((links[i].guid == links[i - 1].guid) && (links[i].fileID == links[i - 1].fileID)) {
									links.RemoveAt(i);
									--i;
									--iMax;
								}
							}
						}
					}
					links.Sort(linksSortComparison);
				}

				IncrementAnalyzeProgress();
			}
		}

		/// <summary>
		/// 逆リンク判定
		/// </summary>
		private void AnalyzeForInboundLink() {
			SetAnalyzeProgressRange(analyzeProgressInboundLink, analyzeData.Count * 2);

			foreach(var dat in analyzeData) {
				if (IsSpritePackingTag(dat.Key)) {
					IncrementAnalyzeProgress();
					continue;
				}
				if (dat.Value.links != null) {
#if SEARCH_TOOLS_DEBUG
					foreach(var link2 in dat.Value.links) {
						var link = link2;
						if (!analyzeData.ContainsKey(link)) {
							var l = new AssetUniqueID(link.guid);
							var noFileID = analyzeData.ContainsKey(l);
							Debug.Log(link + ":" + ((noFileID)? "O": "X") + ":    " + guidToPath[link.guid]);
							if (!noFileID) {
								continue;
							} else {
								link = l;
							}
						}
#else
					foreach(var link in dat.Value.links) {
#endif
						var inboundLinkObject = analyzeData[link];
						if (inboundLinkObject.inboundLinks != null) {
							inboundLinkObject.inboundLinks.Add(dat.Key);
						} else {
							inboundLinkObject.inboundLinks = new List<AssetUniqueID>(){dat.Key};
						}
					}
				}
				IncrementAnalyzeProgress();
			}
			foreach(var assetInfo in analyzeData.Values) {
				if (assetInfo.inboundLinks != null) {
					assetInfo.inboundLinks.Sort(linksSortComparison);
				}
				IncrementAnalyzeProgress();
			}
		}

		/// <summary>
		/// アセットバンドル属性付与
		/// </summary>
		private void AddAssetBundleFlag() {
			if (0 == assetPathsFromAssetBundle.Count) {
				SetAnalyzeProgressRange(analyzeProgressAddAssetBundleFlag, 0);
			} else {
				SetAnalyzeProgressRange(analyzeProgressAddAssetBundleFlag, assetPathsFromAssetBundle.Count * 2 + analyzeData.Count);

				//アセットバンドルの解析ノード登録と梱包アセットパスの逆引き辞書構築
				var guidToAssetBundleAssetInfo = new Dictionary<string, AssetUniqueID>();
				foreach(var assetBundle in assetPathsFromAssetBundle) {
					var assetBundleUniqueID = ConvertAssetBundleToUniqueID(assetBundle.Key);
					var assetBundleAssetInfo = new AssetInfo(IncludeStateFlags.AssetBundle, new List<AssetUniqueID>(), null);
					analyzeData.Add(assetBundleUniqueID, assetBundleAssetInfo);

					foreach(var path in assetBundle.Value) {
						if (pathToGuid.ContainsKey(path)) {
							var guid = pathToGuid[path];
							guidToAssetBundleAssetInfo.Add(guid, assetBundleUniqueID);
						}
					}
					IncrementAnalyzeProgress();
				}
				//アセットバンドル間のリンク・逆リンク構築
				foreach(var assetBundle in assetPathsFromAssetBundle) {
					var assetBundleUniqueID = ConvertAssetBundleToUniqueID(assetBundle.Key);
					var assetBundleAssetInfo = analyzeData[assetBundleUniqueID];
					foreach(var link in assetBundleLinks[assetBundle.Key]) {
						var linkAssetBundleUniqueID = ConvertAssetBundleToUniqueID(link);
						assetBundleAssetInfo.links.Add(linkAssetBundleUniqueID);

						var linkAssetInfo = analyzeData[linkAssetBundleUniqueID];
						if (linkAssetInfo.inboundLinks == null) {
							linkAssetInfo.inboundLinks = new List<AssetUniqueID>();
						}
						linkAssetInfo.inboundLinks.Add(assetBundleUniqueID);
					}
					IncrementAnalyzeProgress();
				}
				//アセットバンドルと梱包アセットのリンク・逆リンク構築
				var links = new Queue<AssetUniqueID>();
				foreach (var dat in analyzeData) {
					if (guidToAssetBundleAssetInfo.ContainsKey(dat.Key.guid)) {
						var assetBundleUniqueID = guidToAssetBundleAssetInfo[dat.Key.guid];
						var assetBundleAssetInfo = analyzeData[assetBundleUniqueID];
						assetBundleAssetInfo.links.Add(dat.Key);
						var assetInfo = analyzeData[dat.Key];
						if (assetInfo.inboundLinks == null) {
							assetInfo.inboundLinks = new List<AssetUniqueID>();
						}
						assetInfo.inboundLinks.Add(assetBundleUniqueID);

						links.Enqueue(dat.Key);
						while (0 < links.Count) {
							var linkAssetUniqueID = links.Dequeue();
							var linkObject = analyzeData[linkAssetUniqueID];
							if ((linkObject.state & IncludeStateFlags.AssetBundle) == 0) {
								linkObject.state |= IncludeStateFlags.AssetBundle;
								if (linkObject.links != null) {
									foreach (var nestlinkObject in linkObject.links) {
										links.Enqueue(nestlinkObject);
									}
								}
							}
						}
					}
					IncrementAnalyzeProgress();
				}
				//アセットバンドルのリンク・逆リンクをソート
				foreach(var assetBundle in assetPathsFromAssetBundle) {
					var assetBundleUniqueID = ConvertAssetBundleToUniqueID(assetBundle.Key);
					var assetBundleAssetInfo = analyzeData[assetBundleUniqueID];
					assetBundleAssetInfo.links.Sort(linksSortComparison);
//					IncrementAnalyzeProgress();
				}
			}
		}

		/// <summary>
		/// リンク・逆リンクのソート
		/// </summary>
		private int linksSortComparison(AssetUniqueID x, AssetUniqueID y) {
			int result;
			if (!guidToPath.ContainsKey(y.guid)) {
				result = -1;
			} else if (!guidToPath.ContainsKey(x.guid)) {
				result = 1;
			} else {
				result = string.Compare(guidToPath[x.guid], guidToPath[y.guid]);
				if (result == 0) {
					result = x.fileID - y.fileID;
				}
			}
			return result;
		}
	}
}
