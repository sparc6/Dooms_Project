#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using NodeCanvas.Framework;
using ParadoxNotion.Serialization;
using System.Collections.Generic;
using NodeCanvas.DialogueTrees;
using ParadoxNotion.Design;
using System.Text;
using System.Linq;
using ParadoxNotion;

namespace NodeCanvas.Editor
{

    public class GraphLocalization : EditorWindow
    {

        private bool willRepaint;
        private Vector2 scrollPos;
        private List<Statement> statements;
        private Locales currentLocale;

        private Graph targetGraph => GraphEditor.currentGraph;

        ///----------------------------------------------------------------------------------------------

        //...
        public static void ShowWindow() {
            var editor = GetWindow<GraphLocalization>();
            editor.GatherStatements();
            editor.Show();
        }

        //...
        void OnEnable() {
            titleContent = new GUIContent("Localization Editor", StyleSheet.canvasIcon);

            minSize = new Vector2(760, 200);
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            Graph.onGraphSerialized -= OnGraphSerialized;
            Graph.onGraphSerialized += OnGraphSerialized;
            GraphEditor.onCurrentGraphChanged -= OnGraphChanged;
            GraphEditor.onCurrentGraphChanged += OnGraphChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            GatherStatements();

            willRepaint = true;
        }

        //...
        void OnDisable() {
            Graph.onGraphSerialized -= OnGraphSerialized;
            GraphEditor.onCurrentGraphChanged -= OnGraphChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        //...
        void OnGraphChanged(Graph graph) {
            GatherStatements();
        }

        //...
        void OnGraphSerialized(Graph graph) {
            if ( graph == targetGraph ) {
                GatherStatements();
                willRepaint = true;
            }
        }

        //...
        void OnUndoRedoPerformed() {
            GatherStatements();
        }

        //...
        void GatherStatements() {

            statements = new List<Statement>();

            if ( targetGraph == null ) { return; }

            JSONSerializer.SerializeAndExecuteNoCycles(typeof(NodeCanvas.Framework.Internal.GraphSource), targetGraph.GetGraphSource(), (o, d) =>
            {
                if ( o is Statement statement ) {
                    statements.Add(statement);
                }
            });
        }


        ///----------------------------------------------------------------------------------------------

        //...
        void OnGUI() {
            EditorGUILayout.HelpBox("1) Select the language you wish to view and edit.\n2) If there is no data for the specified language, hit the Append Language button to add the currently selected language.\n3) If you wish to remove the currently selected language, hit the Remove Language.\n4) Alternatively, you can export a .tsv file and edit all text localizations in an external spreadsheet software then import them back.\nNote: The Default language is the one used by default in the graph editor.", MessageType.Info);
            if ( targetGraph == null ) {
                ShowNotification(new GUIContent("No Graph Open in Graph Editor"));
                return;
            }
            RemoveNotification();

            GUILayout.BeginHorizontal();
            currentLocale = (Locales)UnityEditor.EditorGUILayout.EnumPopup(currentLocale);
            GUI.enabled = currentLocale != Locales.Default;
            if ( GUILayout.Button("Append Language", GUILayout.Width(150)) ) {
                if ( currentLocale != Locales.Default ) {
                    Undo.RecordObject(targetGraph, "Append Language");
                    foreach ( var statement in statements ) {
                        if ( statement.localizations == null ) statement.localizations = new Dictionary<Locales, Statement.Localization>();
                        if ( !statement.localizations.ContainsKey(currentLocale) ) {
                            statement.localizations.Add(currentLocale, new Statement.Localization());
                        }

                    }
                }
            }

            if ( GUILayout.Button("Remove Language", GUILayout.Width(150)) ) {
                if ( currentLocale != Locales.Default ) {
                    Undo.RecordObject(targetGraph, "Remove Language");
                    foreach ( var statement in statements ) {
                        if ( statement.localizations != null ) {
                            statement.localizations.Remove(currentLocale);
                            if ( statement.localizations.Count == 0 ) {
                                statement.localizations = null;
                            }
                        }
                    }
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            ///----------------------------------------------------------------------------------------------
            GUILayout.BeginHorizontal();
            if ( GUILayout.Button("Export TSV") ) {
                var path = EditorUtility.SaveFilePanelInProject("Export to TSV", targetGraph.agent ? targetGraph.agent.name : targetGraph.name, "tsv", string.Empty);
                var delimiter = "\t";
                if ( !string.IsNullOrEmpty(path) ) {
                    var sb = new StringBuilder();
                    sb.Append("UID");
                    foreach ( var locName in System.Enum.GetNames(typeof(Locales)) ) {
                        sb.Append(delimiter + locName);
                    }
                    foreach ( var statement in statements ) {
                        sb.Append("\n");
                        sb.Append(statement.UID);
                        foreach ( var locName in System.Enum.GetNames(typeof(Locales)) ) {
                            var locale = System.Enum.Parse<Locales>(locName);
                            var localized = statement.GetLocalizedText(locale);
                            if ( localized == statement.text && locale != Locales.Default ) { localized = string.Empty; }
                            sb.Append(delimiter + "\"" + localized + "\"");
                        }
                    }
                    System.IO.File.WriteAllText(path, sb.ToString());
                    AssetDatabase.Refresh();
                }
            }

            if ( GUILayout.Button("Import TSV") ) {
                var path = EditorUtility.OpenFilePanel("Import from TSV", "Assets", "tsv");
                if ( !string.IsNullOrEmpty(path) ) {
                    Undo.RecordObject(targetGraph, "Import TSV");
                    //UID   Default     English     Greek
                    //XXX   Hello       Hello       Καλησπερα
                    //XXX   This text   This text   Αυτο το κειμενο
                    var statementsDict = statements.ToDictionary(x => x.UID, x => x);
                    var lines = System.IO.File.ReadAllLines(path);
                    var headerLine = lines[0].Split('\t');

                    for ( var i = 1; i < lines.Length; i++ ) {
                        var cells = lines[i].Split('\t');
                        if ( statementsDict.TryGetValue(cells[0], out Statement statement) ) {
                            for ( var j = 1; j < cells.Length; j++ ) {
                                var locale = System.Enum.Parse<Locales>(headerLine[j]);
                                var parsedText = cells[j];
                                //some software keep the quotes, other don't.
                                if ( parsedText.StartsWith('"') && parsedText.EndsWith('"') ) {
                                    parsedText = parsedText.Substring(1, parsedText.Length - 2);
                                }
                                if ( string.IsNullOrEmpty(parsedText) ) { continue; }
                                if ( locale == Locales.Default ) {
                                    //don't or do overwrite default text? Better don't.
                                    // statement.text = parsedText;
                                    continue;
                                }
                                if ( statement.localizations == null ) { statement.localizations = new Dictionary<Locales, Statement.Localization>(); }
                                if ( statement.localizations.TryGetValue(locale, out Statement.Localization localization) ) {
                                    localization.text = parsedText;
                                } else {
                                    statement.localizations[locale] = new Statement.Localization() { text = parsedText };
                                }
                            }
                        }
                    }
                }
            }
            GUILayout.EndHorizontal();
            ///----------------------------------------------------------------------------------------------


            EditorUtils.BoldSeparator();

            if ( statements == null || statements.Count == 0 ) {
                ShowNotification(new GUIContent("The graph has no localizable statements"));
                return;
            }

            GUILayout.BeginHorizontal();
            EditorUtils.CoolLabel("Default");
            if ( currentLocale != Locales.Default ) {
                EditorUtils.CoolLabel(currentLocale.ToString());
            }
            GUILayout.EndHorizontal();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, false, false);

            foreach ( var statement in statements ) {
                GUILayout.BeginHorizontal();
                GUILayout.Space(10);

                GUILayout.BeginVertical();
                statement.text = UnityEditor.EditorGUILayout.TextArea(statement.text, Styles.wrapTextArea, GUILayout.Width(350), GUILayout.ExpandWidth(true), GUILayout.Height(50));
                statement.audio = UnityEditor.EditorGUILayout.ObjectField(statement.audio, typeof(AudioClip), false, GUILayout.Width(350), GUILayout.ExpandWidth(true)) as AudioClip;
                GUILayout.EndVertical();

                GUILayout.Space(20);

                if ( currentLocale != Locales.Default ) {
                    if ( statement.localizations != null ) {
                        if ( statement.localizations.TryGetValue(currentLocale, out Statement.Localization localization) ) {
                            GUILayout.BeginVertical();
                            localization.text = UnityEditor.EditorGUILayout.TextArea(localization.text, Styles.wrapTextArea, GUILayout.Width(350), GUILayout.ExpandWidth(true), GUILayout.Height(50));
                            localization.audio = UnityEditor.EditorGUILayout.ObjectField(localization.audio, typeof(AudioClip), false, GUILayout.Width(350), GUILayout.ExpandWidth(true)) as AudioClip;
                            GUILayout.EndVertical();
                        } else {
                            GUILayout.Label("No " + currentLocale + " localization exists for this statement.\nPress Append Language to add it.", GUILayout.Width(350), GUILayout.ExpandWidth(true));
                        }
                    } else {
                        GUILayout.Label("No " + currentLocale + " localization exists for this statement.\nPress Append Language to add it.", GUILayout.Width(350), GUILayout.ExpandWidth(true));
                    }
                }

                GUILayout.Space(10);
                GUILayout.EndHorizontal();
                EditorUtils.Separator();
            }

            EditorGUILayout.EndScrollView();

            if ( willRepaint ) {
                Repaint();
            }

            if ( GUI.changed ) {
                targetGraph.SelfSerialize();
                UndoUtility.SetDirty(targetGraph);
            }
        }
    }
}

#endif