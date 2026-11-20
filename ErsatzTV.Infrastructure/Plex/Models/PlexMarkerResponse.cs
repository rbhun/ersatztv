using System.Xml.Serialization;

namespace ErsatzTV.Infrastructure.Plex.Models;

public class PlexMarkerResponse
{
    [XmlAttribute("type")]
    public string Type { get; set; }

    [XmlAttribute("startTimeOffset")]
    public long StartTimeOffset { get; set; }

    [XmlAttribute("endTimeOffset")]
    public long EndTimeOffset { get; set; }

    [XmlAttribute("final")]
    public bool Final { get; set; }
}
