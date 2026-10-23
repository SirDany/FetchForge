
namespace FetchForge.Core.Models;

public sealed class AudioProfile
{
    public string Name { get; set; } = "Default";
    public bool Enabled { get; set; } = true;

    public string FilterChain { get; set; } =
        "highpass=f=60,equalizer=f=150:t=q:w=1:g=3," +
        "equalizer=f=350:t=q:w=1:g=-1," +
        "equalizer=f=2800:t=q:w=1:g=2," +
        "treble=f=6500:g=1.5," +
        "loudnorm=I=-18:TP=-2:LRA=11";

    public string OutputCodec { get; set; } = "libmp3lame";
    public string OutputQuality { get; set; } = "2";
}
