using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.Tasks.Actions
{

    [Category("Audio")]
    public class PlayAudioAtPosition : ActionTask<Transform>
    {

        [RequiredField]
        public BBParameter<AudioClip> audioClip;
        [SliderField(0, 1)]
        public float volume = 1;
        public bool waitActionFinish;

        protected override string info => "Play " + audioClip.ToString();
        public override float length => !audioClip.isNull && waitActionFinish ? audioClip.value.length : 0;

        protected override void OnExecute() {
            AudioSource.PlayClipAtPoint(audioClip.value, agent.position, volume);
            if ( !waitActionFinish ) {
                EndAction();
            }
        }

        protected override void OnUpdate() {
            if ( elapsedTime >= audioClip.value.length ) {
                EndAction();
            }
        }
    }
}