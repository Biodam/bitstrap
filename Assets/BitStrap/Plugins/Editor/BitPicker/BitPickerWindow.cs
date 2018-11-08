using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;

namespace BitStrap
{
	public struct BitPickerItem
	{
		public static readonly Texture2D EmptyIcon = new Texture2D( 4, 4 );

		public readonly BitPickerProvider provider;
		public readonly string name;
		public readonly string fullName;

		public readonly object data;
		public Texture2D icon;

		public BitPickerItem( BitPickerProvider provider, string name, string fullName, object data )
		{
			this.provider = provider;
			this.name = name;
			this.fullName = fullName;

			this.data = data;
			this.icon = null;
		}
	}

	public sealed class BitPickerWindow : EditorWindow
	{
		private static class Consts
		{
			public const string SearchControlName = "BitPickerSearch";
			public const int PatternFontSize = 18;
			public const int MaxResults = 10;
			public const float WindowHeightOffset = 109.0f;

			public static readonly float WindowWidth = 600.0f;
			public static readonly Color SelectionColor = new Color32( 62, 95, 150, 255 );
			public static readonly Color InterleavedBackgroundColor = new Color( 0.0f, 0.0f, 0.0f, 0.1f );
		}

		private struct Result
		{
			readonly public int score;
			readonly public int itemIndex;
			readonly public List<byte> nameMatches;
			readonly public List<byte> fullNameMatches;

			public Result( int score, int itemIndex, List<byte> nameMatches, List<byte> fullNameMatches )
			{
				this.score = score;
				this.itemIndex = itemIndex;
				this.nameMatches = nameMatches;
				this.fullNameMatches = fullNameMatches;
			}
		}

		private static bool editorReloaded = true;

		private BitPickerConfig config;
		private bool initialized = false;
		private string pattern = "";
		private List<BitPickerItem> providedItems = new List<BitPickerItem>( 2048 );
		private List<Result> results = new List<Result>( 1024 );
		private int selectedResultIndex = 0;

		private GUIStyle patternStyle;
		private GUIStyle nameStyle;
		private GUIStyle fullNameStyle;
		private GUIStyle[] sourceStyles;

		private StringBuilder contentCache = new StringBuilder();
		private float patternStyleHeightCache;
		private float nameStyleHeightCache;

		[MenuItem( "Window/BitStrap/BitPicker %k" )]
		public static void Open()
		{
			editorReloaded = false;

			BitPickerConfig config;
			if( !BitPickerConfig.Instance.TryGet( out config ) )
				return;

			var window = ScriptableObject.CreateInstance<BitPickerWindow>();
			window.config = config;

			window.position = new Rect(
				( Screen.currentResolution.width - Consts.WindowWidth ) * 0.5f,
				Consts.WindowHeightOffset,
				Consts.WindowWidth,
				EditorGUIUtility.singleLineHeight
			);

			window.ShowPopup();
			EditorWindow.FocusWindowIfItsOpen<BitPickerWindow>();
		}

		public void Init()
		{
			patternStyle = new GUIStyle( EditorStyles.textField );
			patternStyle.fontSize = Consts.PatternFontSize;
			patternStyle.margin = new RectOffset( 0, 0, 0, 0 );

			nameStyle = new GUIStyle( EditorStyles.largeLabel );
			nameStyle.alignment = TextAnchor.MiddleLeft;
			nameStyle.richText = true;

			fullNameStyle = new GUIStyle( EditorStyles.label );
			fullNameStyle.alignment = TextAnchor.MiddleLeft;
			fullNameStyle.richText = true;

			sourceStyles = new GUIStyle[] {
				GUI.skin.GetStyle( "sv_label_0" ),
				GUI.skin.GetStyle( "sv_label_1" ),
				GUI.skin.GetStyle( "sv_label_2" ),
				GUI.skin.GetStyle( "sv_label_3" ),
				GUI.skin.GetStyle( "sv_label_4" ),
				GUI.skin.GetStyle( "sv_label_5" ),
				GUI.skin.GetStyle( "sv_label_6" ),
				GUI.skin.GetStyle( "sv_label_7" ),
			};

			patternStyleHeightCache = BitPickerHelper.GetStyleLayoutSize( patternStyle, GUIContent.none ).y + 1;
			nameStyleHeightCache = BitPickerHelper.GetStyleLayoutSize( nameStyle, GUIContent.none ).y + 1;
			minSize = new Vector2( position.size.x, patternStyleHeightCache );

			foreach( var provider in config.providers )
				provider.Provide( providedItems );
		}

		public void OnGUI()
		{
			if( editorReloaded )
			{
				Close();
				return;
			}

			if( !initialized )
				Init();

			// Events
			{
				var currentEvent = Event.current;

				if( currentEvent.type == EventType.KeyDown )
				{
					var resultsCount = Mathf.Min( Consts.MaxResults, results.Count );

					switch( currentEvent.keyCode )
					{
					case KeyCode.Escape:
						Close();
						break;
					case KeyCode.UpArrow:
						selectedResultIndex = ( selectedResultIndex - 1 + resultsCount ) % resultsCount;
						currentEvent.Use();
						break;
					case KeyCode.DownArrow:
						selectedResultIndex = ( selectedResultIndex + 1 ) % resultsCount;
						currentEvent.Use();
						break;
					case KeyCode.Return:
						if( currentEvent.control || currentEvent.shift )
							PingItem( providedItems[results[selectedResultIndex].itemIndex] );
						else
							SelectItem( providedItems[results[selectedResultIndex].itemIndex] );

						currentEvent.Use();
						break;
					}
				}
			}

			// Pattern matching
			{
				EditorGUI.BeginChangeCheck();

				GUI.SetNextControlName( Consts.SearchControlName );
				var patternRect = GUILayoutUtility.GetRect( GUIContent.none, patternStyle );
				pattern = EditorGUI.TextField( patternRect, pattern, patternStyle );

				if( EditorGUI.EndChangeCheck() )
				{
					selectedResultIndex = 0;
					results.Clear();

					if( pattern.Length > 0 )
					{
						var patternWithoutArgs = BitPickerHelper.RemoveArgs( pattern );

						for( int i = 0; i < providedItems.Count; i++ )
						{
							var item = providedItems[i];

							int nameScore;
							var nameMatches = new List<byte>();
							var nameMatched = FuzzyFinder.Match(
								config.fuzzyFinderConfig,
								patternWithoutArgs,
								item.name,
								out nameScore,
								nameMatches
							);

							int fullNameScore;
							var fullNameMatches = new List<byte>();
							var fullNameMatched = FuzzyFinder.Match(
								config.fuzzyFinderConfig,
								patternWithoutArgs,
								item.fullName,
								out fullNameScore,
								fullNameMatches
							);

							if( nameMatched || fullNameMatched )
							{
								var score = Mathf.Max(
									nameScore + config.scoreConfig.nameMatchBonus,
									fullNameScore
								);

								results.Add( new Result( score, i, nameMatches, fullNameMatches ) );
							}
						}

						results.Sort( ( a, b ) => b.score - a.score );
					}
				}
			}

			// Show Results
			{
				var resultsCount = Mathf.Min( Consts.MaxResults, results.Count );

				var windowRect = position;
				windowRect.height = patternStyleHeightCache + resultsCount * nameStyleHeightCache;
				position = windowRect;
				minSize = position.size;

				for( var i = 0; i < resultsCount; i++ )
				{
					var result = results[i];
					var item = providedItems[result.itemIndex];
					if( item.icon == null )
						item.icon = item.provider.GetItemIcon( item );

					contentCache.Length = 0;
					if( config.showScores )
					{
						contentCache.Append( result.score );
						contentCache.Append( " - " );
					}

					BitPickerHelper.HighlightMatches( item.name, result.nameMatches, contentCache );
					var nameContent = new GUIContent( contentCache.ToString() );

					contentCache.Length = 0;
					BitPickerHelper.HighlightMatches( item.fullName, result.fullNameMatches, contentCache );
					var fullNameContent = new GUIContent( contentCache.ToString() );

					var nameSize = BitPickerHelper.GetStyleLayoutSize( nameStyle, nameContent );

					var resultRect = GUILayoutUtility.GetRect( position.width, nameSize.y );

					var sourceContent = new GUIContent( item.provider.GetProvisionSource() );
					var sourceStyle = GetSourceStyle( sourceContent.text );
					var sourceSize = BitPickerHelper.GetStyleLayoutSize( sourceStyle, sourceContent );

					if( selectedResultIndex == i )
						EditorGUI.DrawRect( resultRect, Consts.SelectionColor );
					else if( i % 2 == 1 )
						EditorGUI.DrawRect( resultRect, Consts.InterleavedBackgroundColor );

					Rect iconRect;
					Rect sourceRect;
					Rect nameRect;
					Rect fullNameRect;

					resultRect
						.Left( resultRect.height, out iconRect )
						.Left( nameSize.x, out nameRect )
						.Right( sourceSize.x, out sourceRect )
						.Expand( out fullNameRect );

					sourceRect = sourceRect.CenterVertically( sourceSize.y );

					if( item.icon != BitPickerItem.EmptyIcon )
						GUI.Label( iconRect, item.icon );

					GUI.Label( sourceRect, sourceContent, sourceStyle );
					GUI.Label( fullNameRect, fullNameContent, fullNameStyle );

					if( GUI.Button( nameRect, nameContent, nameStyle ) )
						SelectItem( item );
				}
			}

			if( !initialized )
			{
				EditorGUI.FocusTextInControl( Consts.SearchControlName );
				initialized = true;
			}
		}

		private void OnLostFocus()
		{
			Close();
		}

		private void PingItem( BitPickerItem item )
		{
			if( item.provider != null )
				item.provider.OnPingItem( item );
		}

		private void SelectItem( BitPickerItem item )
		{
			Close();

			if( item.provider != null )
				item.provider.OnOpenItem( item, pattern );
		}

		private GUIStyle GetSourceStyle( string source )
		{
			var hash = source.GetHashCode();
			var count = sourceStyles.Length;
			var index = ( ( hash % count ) + count ) % count;
			return sourceStyles[index];
		}
	}
}