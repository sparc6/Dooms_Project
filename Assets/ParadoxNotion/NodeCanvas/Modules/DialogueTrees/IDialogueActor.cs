using UnityEngine;


namespace NodeCanvas.DialogueTrees
{

    ///<summary> An interface to use for DialogueActors within a DialogueTree.</summary>
	public interface IDialogueActor
    {
        string name { get; }
        Texture2D portrait { get; }
        Sprite portraitSprite { get; }
        Color dialogueColor { get; }
        Vector3 dialoguePosition { get; }
        Transform transform { get; }
    }

    ///<summary>A basic rather limited implementation of IDialogueActor</summary>
    [System.Serializable]
    public class ProxyDialogueActor : IDialogueActor
    {

        private string _name;
        private Transform _transform;

        public string name => _name;

        public Texture2D portrait => null;

        public Sprite portraitSprite => null;

        public Color dialogueColor => Color.white;

        public Vector3 dialoguePosition => Vector3.zero;

        public Transform transform => _transform;

        public ProxyDialogueActor(string name, Transform transform) {
            this._name = name;
            this._transform = transform;
        }
    }
}