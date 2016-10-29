using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace SearchTools {
	public class LinkAnalyzer : System.IDisposable {

		/// <summary>
		/// IsInclude戻り値
		/// </summary>
		public enum IsIncludeReturn {
			False,
			True,
			Unknown,
		}

		/// <summary>
		/// 解析中確認
		/// </summary>
		public  bool analyzing {get{
			return analyzeThread != null;
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
			if(analyzeThread != null) {
				analyzeThread.Abort(); 
			}
		}

		/// <summary>
		/// パスの梱包確認
		/// </summary>
		public IsIncludeReturn IsIncludeFromPath(string path) {
			var result = IsIncludeReturn.Unknown;
			if (analyzeData.ContainsKey(path)) {
				result = analyzeData[path].state;
			}
			return result;
		}

		/// <summary>
		/// スプライトパッキングタグの梱包確認
		/// </summary>
		public IsIncludeReturn IsIncludeFromSpritePackingTag(string tag) {
			return IsIncludeFromPath(GetPathFromSpritePackingTag(tag));
		}

		/// <summary>
		/// リンク取得
		/// </summary>
		public List<string> GetLinks(string path) {
			List<string> result = null;
			if (analyzeData.ContainsKey(path) && (analyzeData[path].state != IsIncludeReturn.Unknown)) {
				result = analyzeData[path].links;
			}
			return result;
		}

		/// <summary>
		/// 逆リンク取得
		/// </summary>
		public List<string> GetInboundLinks(string path) {
			List<string> result = null;
			if (!analyzing && analyzeData.ContainsKey(path) && (analyzeData[path].state != IsIncludeReturn.Unknown)) {
				result = analyzeData[path].inboundLinks;
			}
			return result;
		}

		/// <summary>
		/// パスからスプライトパッキングタグ取得
		/// </summary>
		public string GetSpritePackingTag(string path) {
			string result = null;
			if (!analyzing && analyzeData.ContainsKey(path) && (analyzeData[path].state != IsIncludeReturn.Unknown)) {
				result = analyzeData[path].spritePackingTag;
			}
			return result;
		}

		/// <summary>
		/// 開始
		/// </summary>
		public void Start() {
			if (analyzeProgress == 0.0f) {
				dataBasePath = Application.dataPath;
				dataBasePath = dataBasePath.Substring(0, dataBasePath.Length - 6); //末端の"Assets"を削除

				includeScenes = EditorBuildSettings.scenes.Where(x=>x.enabled)
														.Select(x=>x.path)
														.ToList();
				if (includeScenes.Count == 0) {
					var activeScenePath = EditorSceneManager.GetActiveScene().path;
					if (!string.IsNullOrEmpty(activeScenePath)) {
						includeScenes.Add(activeScenePath);
					}
				}

				analyzeData = AssetDatabase.GetAllAssetPaths()
											.Where(x=>x.StartsWith(assetsPrefix))
											.ToDictionary(x=>x, x=>new AssetInfo());

				guidToPath  = analyzeData.Keys.ToDictionary(x=>AssetDatabase.AssetPathToGUID(x), x=>x);

				analyzeThread = new Thread(Analyze);
				analyzeThread.Start();
			}
		}

		/// <summary>
		/// 解析スレッド
		/// </summary>
		private Thread analyzeThread = null;

		/// <summary>
		/// 解析進捗
		/// </summary>
		private float analyzeProgress = 0.0f;

		/// <summary>
		/// 解析パス
		/// </summary>
		private string dataBasePath = null;

		/// <summary>
		/// GUIDマッチング正規表現
		/// </summary>
		private Regex guidMatchRegex = new Regex(@"guid:[\s&&[^\r\n]]*([0-9a-zA-Z]{32})");

		/// <summary>
		/// SpritePackingTagマッチング正規表現
		/// </summary>
		private Regex spritePackingTagMatchRegex = new Regex(@"spritePackingTag:[\s&&[^\r\n]]*(.*)");

		/// <summary>
		/// リンク情報
		/// </summary>
		private struct LinkInfo {
			public List<string> links;
			public string spritePackingTag;
		}

		/// <summary>
		/// アセット情報
		/// </summary>
		private class AssetInfo {
			public IsIncludeReturn state;
			public LinkInfo linkInfo;
			public List<string> links {get{return linkInfo.links;} set{linkInfo.links = value;}}
			public List<string> inboundLinks;
			public string spritePackingTag {get{return linkInfo.spritePackingTag;} set{linkInfo.spritePackingTag = value;}}

			public AssetInfo() {
				state = IsIncludeReturn.Unknown;
				linkInfo = new LinkInfo(){links = null, spritePackingTag = null};
				inboundLinks = null;
			}
			public AssetInfo(IsIncludeReturn state, List<string> links, string spritePackingTag) {
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
		/// 解析結果
		/// </summary>
		private Dictionary<string, AssetInfo> analyzeData = null;

		/// <summary>
		/// 梱包シーン
		/// </summary>
		private List<string> includeScenes = null;

		/// <summary>
		/// GUIDパス変換辞書
		/// </summary>
		private Dictionary<string, string> guidToPath = null;

		/// <summary>
		/// 解析開始
		/// </summary>
		private void Analyze() {
			//スクリプト梱包判定
			var scriptsCount = AnalyzeForScript();

			var progressUnit = 1.0f / (analyzeData.Count - scriptsCount + 1);
			var doneCount = 0.0f;

			//アセット梱包判定
			AnalyzeForAsset(ref doneCount, progressUnit);

			//残りを除外判定
			ExcludeForLeftovers(ref doneCount, progressUnit);

			//逆リンク判定
			AnalyzeForInboundLink(ref doneCount, progressUnit);

			analyzeThread = null;
		}

		/// <summary>
		/// スクリプト梱包判定
		/// </summary>
		private int AnalyzeForScript() {
			var result = 0;
			foreach(var dat in analyzeData) {
				if (dat.Key.EndsWith(".cs") || dat.Key.EndsWith(".js")) {
					if (-1 == dat.Key.IndexOf("/Editor/")) {
						dat.Value.state = IsIncludeReturn.True;
					} else {
						dat.Value.state = IsIncludeReturn.False;
					}
					++result;
				}
			}
			return result;
		}

		/// <summary>
		/// アセット梱包判定
		/// </summary>
		private void AnalyzeForAsset(ref float doneCount, float progressUnit) {
			//信頼されたルートの検索
			var analyzeQueue = GetTrustedRootPath();

			while (0 < analyzeQueue.Count) {
				var path = analyzeQueue.Dequeue();
				var dat = analyzeData[path];

				if (dat.state == IsIncludeReturn.Unknown) {
					dat.linkInfo = GetLinkPathsFromAsset(path);

					if (dat.links != null) {
						dat.links.ForEach(x=>analyzeQueue.Enqueue(x));
					}

					if (!string.IsNullOrEmpty(dat.spritePackingTag)) {
						var spritePackingTagPath = GetPathFromSpritePackingTag(dat.spritePackingTag);
						if (analyzeData.ContainsKey(spritePackingTagPath)) {
							analyzeData[spritePackingTagPath].links.Add(path);
						} else {
							analyzeData.Add(spritePackingTagPath, new AssetInfo(IsIncludeReturn.True
																				, new List<string>(){path}
																				, null
																				)
											);
						}
					}
					dat.state = IsIncludeReturn.True;

					++doneCount;
					analyzeProgress = doneCount * progressUnit;
				}
			}
		}

		/// <summary>
		/// 信頼されたルートパスを取得
		/// </summary>
		private Queue<string> GetTrustedRootPath() {
			var result = new Queue<string>();

			//Scenes In Build
			includeScenes.ForEach(x=>result.Enqueue(x));

			//Always Included Shaders
			foreach (var path in GetLinkPathsFromAsset("ProjectSettings/GraphicsSettings.asset").links) {
				result.Enqueue(path);
			}

			//Resources
			foreach (var path in analyzeData.Keys) {
				if (0 <= path.IndexOf("/Resources/")) {
					result.Enqueue(path);
				}
			}

			return result;
		}

		/// <summary>
		/// アセットからリンクパスを取得
		/// </summary>
		private LinkInfo GetLinkPathsFromAsset(string path) {
			var result = new LinkInfo();

			string text;
			var linkerType = GetLinkerType(path);
			switch (linkerType) {
			case GetLinkerTypeReturn.AssetLinker:
				text = readTextFromAsset(path);
				break;
			case GetLinkerTypeReturn.MetaLinker:
			case GetLinkerTypeReturn.TextureLinker:
				text = readTextFromAsset(path + ".meta");
				break;
			case GetLinkerTypeReturn.NoLinker:
			default:
				text = null;
				break;
			}
			if (string.IsNullOrEmpty(text)) {
				return result;
			}

			//SpritePackingTag取得
			if (linkerType == GetLinkerTypeReturn.TextureLinker) {
				var matche = spritePackingTagMatchRegex.Match(text);
				var spritePackingTag = matche.Groups[1].Value;
				if (!string.IsNullOrEmpty(spritePackingTag)) {
					if (spritePackingTag[0] == '\'') {
						spritePackingTag = spritePackingTag.Substring(1, spritePackingTag.Length - 2).Replace("''", "'");
					}
					result.spritePackingTag = spritePackingTag;
				}
			}

			var links = new List<string>();
			{
				//リンクGUID列挙
				var matches = guidMatchRegex.Matches(text);
				foreach(Match matche in matches) {
					var guid = matche.Groups[1].Value;
					links.Add(guid);
				}

				//重複削除
				links.Sort();
				for (var i = links.Count - 2; 0 <= i; --i) {
					if (links[i] == links[i + 1]) {
						links.RemoveAt(i + 1);
					}
				}

				//path化
				for (var i = links.Count - 1; 0 <= i; --i) {
					var guid = links[i];
					if (!guidToPath.ContainsKey(guid)) {
						//path化出来ないなら削除
						links.RemoveAt(i);
						continue;
					}
					var linkPath = guidToPath[guid];
					if (linkPath == path) {
						//自身なら削除
						links.RemoveAt(i);
						continue;
					}
					links[i] = linkPath;
				}
			}
			result.links = links;

			return result;
		}

		/// <summary>
		/// GetLinkerType戻り値
		/// </summary>
		private enum GetLinkerTypeReturn {
			NoLinker,
			AssetLinker,
			MetaLinker,
			TextureLinker,
		}

		/// <summary>
		/// リンカー確認
		/// </summary>
		private GetLinkerTypeReturn GetLinkerType(string path) {
			var result = GetLinkerTypeReturn.NoLinker;

			var extStartIndex = path.LastIndexOf('.');
			if (0 <= extStartIndex) {
				var ext = path.Substring(extStartIndex, path.Length - extStartIndex);
				switch (ext) {
				case ".anim":
				case ".asset":
				case ".controller":
				case ".cubemap":
				case ".flare":
				case ".fontsettings":
				case ".guiskin":
				case ".mask":
				case ".mat":
				case ".mixer":
				case ".overrideController":
				case ".prefab":
				case ".unity":
					result = GetLinkerTypeReturn.AssetLinker;
					break;
				case ".fbx":
					result = GetLinkerTypeReturn.MetaLinker;
					break;
				case ".bmp":
				case ".gif":
				case ".iff":
				case ".jpg": case ".jpeg":
				case ".pic": case ".pict":
				case ".png":
				case ".psd":
				case ".tif": case ".tiff":
				case ".tga":
					result = GetLinkerTypeReturn.TextureLinker;
					break;
				case ".giparams":
				case ".physicMaterial":
				case ".physicsMaterial2D":
				case ".renderTexture":
					result = GetLinkerTypeReturn.NoLinker;
					break;
				}
			}
			return result;
		}

		/// <summary>
		/// アセットからのテキスト読み込み
		/// </summary>
		private string readTextFromAsset(string path) {
			string result = null;
			try {
				using (var sr = new StreamReader(dataBasePath + path)) {
					result = sr.ReadToEnd();
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
		/// SpritePackingTagからパス取得
		/// </summary>
		private static string GetPathFromSpritePackingTag(string spritePackingTag) {
			return spritePackingTagsPrefix + spritePackingTag;
		}

		/// <summary>
		/// 残りを除外判定
		/// </summary>
		private void ExcludeForLeftovers(ref float doneCount, float progressUnit) {
			var analyzeAppend = new Dictionary<string, AssetInfo>();
			foreach(var dat in analyzeData) {
				if (dat.Value.state == IsIncludeReturn.Unknown) {
					dat.Value.linkInfo = GetLinkPathsFromAsset(dat.Key);

					if (string.IsNullOrEmpty(dat.Value.spritePackingTag)) {
						dat.Value.state = IsIncludeReturn.False;
					} else {
						var spritePackingTagPath = GetPathFromSpritePackingTag(dat.Value.spritePackingTag);
						if (analyzeData.ContainsKey(spritePackingTagPath)) {
							analyzeData[spritePackingTagPath].links.Add(dat.Key);
							dat.Value.state = IsIncludeReturn.True;
						} else if (analyzeAppend.ContainsKey(spritePackingTagPath)) {
							analyzeAppend[spritePackingTagPath].links.Add(dat.Key);
							dat.Value.state = IsIncludeReturn.False;
						} else {
							analyzeAppend.Add(spritePackingTagPath, new AssetInfo(IsIncludeReturn.False
																				, new List<string>(){dat.Key}
																				, null
																				)
											);
							dat.Value.state = IsIncludeReturn.False;
						}
					}

					++doneCount;
					analyzeProgress = doneCount * progressUnit;
				}
			}
			foreach(var dat in analyzeAppend) {
				analyzeData.Add(dat.Key, dat.Value);
			}
		}

		/// <summary>
		/// 逆リンク判定
		/// </summary>
		private void AnalyzeForInboundLink(ref float doneCount, float progressUnit) {
			foreach(var dat in analyzeData) {
				if (IsSpritePackingTagsPath(dat.Key)) {
					continue;
				}
				if (dat.Value.links != null) {
					foreach(var link in dat.Value.links) {
						var inboundLinkObject = analyzeData[link];
						if (inboundLinkObject.inboundLinks != null) {
							inboundLinkObject.inboundLinks.Add(dat.Key);
						} else {
							inboundLinkObject.inboundLinks = new List<string>(){dat.Key};
						}
					}
				}
			}

			++doneCount;
			analyzeProgress = doneCount * progressUnit;
		}

		/// <summary>
		/// アセットパス確認
		/// </summary>
		private static bool IsAssetsPath(string path) {
			var result = false;
			if (!string.IsNullOrEmpty(path)) {
				result = path.StartsWith(assetsPrefix);
			}
			return result;
		}

		/// <summary>
		/// スプライトパッキングタグパス確認
		/// </summary>
		private static bool IsSpritePackingTagsPath(string path) {
			var result = false;
			if (!string.IsNullOrEmpty(path)) {
				result = path.StartsWith(spritePackingTagsPrefix);
			}
			return result;
		}
	}
}
