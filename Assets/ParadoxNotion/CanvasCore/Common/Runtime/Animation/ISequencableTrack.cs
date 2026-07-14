namespace ParadoxNotion.Animation
{
    ///<summary>An interface for things that can be sequenced. Works together with the SequencerEditor for editing.</summary>
    public interface ISequencableTrack
    {
        string name { get; }
        float time { get; set; }
        float length { get; }
    }
}