﻿using System.Collections.Generic;
using Core.midi;

namespace Program.midi.scheduler.component.solo;

public class RetrievalSoloist : Soloist
{
    private MidiSong _solo;
    
    public override void Initialize(MidiSong solo, LeadSheet leadSheet)
    {
        _solo = solo;

        // Change output name for all notes
        foreach (var measure in _solo.Measures)
        {
            for (var index = 0; index < measure.Notes.Count; index++)
            {
                measure.Notes[index] = measure.Notes[index] with { OutputName = OutputName.Algorithm };
            }
        }
    }

    public override void IngestMeasures(List<MidiMeasure> measures, int startMeasureNum) { }

    public override List<MidiMeasure> Generate(int generateMeasureCount, int startMeasureNum)
    {
        // Schedule measures from solo
        return _solo.Measures.GetRange(startMeasureNum % _solo.Measures.Count, generateMeasureCount);
    }
}