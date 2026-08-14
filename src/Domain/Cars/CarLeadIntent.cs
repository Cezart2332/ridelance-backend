namespace Domain.Cars;

/// <summary>Ce vrea de fapt cel care a trimis formularul.</summary>
public enum CarLeadIntent
{
    /// <summary>Vrea mașina acum — cazul obișnuit.</summary>
    Request = 0,

    /// <summary>Mașina nu e liberă; vrea să fie anunțat când se eliberează.</summary>
    Waitlist = 1,
}
