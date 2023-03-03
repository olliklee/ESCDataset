﻿namespace EurovisionDataset.Data.Senior;

public class Contest : Data.Contest
{
    public IEnumerable<string> Broadcasters { get; set; }

    // TODO: quitar estos new sin que se pierda inforamción el el json edbido al polimorfismo
    public new IEnumerable<Contestant> Contestants { get; set; }
    public new IEnumerable<Round> Rounds { get; set; }
}
