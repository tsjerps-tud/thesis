﻿using Core.midi;
using Core.models;
using Core.tokens.v1;

namespace Core.algorithm;

public class NoteFactorOracleAlgorithm : IAlgorithm
{
    private MidiMelody _melody;

    private LeadSheet _leadSheet;
    
    public NoteFactorOracle NoteFactorOracle = new();

    public int HumanFourStart;
    
    public void Initialize(MidiSong[] solos, LeadSheet leadSheet)
    {
        _melody = MidiMelody.FromMeasures(solos[0].Measures, leadSheet);

        NoteFactorOracle.AddMelody(_melody);

        _leadSheet = leadSheet;
    }

    public void Learn(List<MidiMeasure> measures, int measureNum = 0)
    {
        HumanFourStart = NoteFactorOracle.Nodes.Count;
        
        var recordedMelody = MidiMelody.FromMeasures(measures, _leadSheet, measureNum);
        
        // Add recorded melody to melody and factor oracle
        _melody += recordedMelody;
        NoteFactorOracle.AddMelody(recordedMelody);
    }

    public List<MidiMeasure> Generate(int generateMeasureCount = 4, int startMeasureNum = 0)
    {
        // Create new measures
        var measures = new List<MidiMeasure>();
        for (var i = 0; i < generateMeasureCount; i++)
            measures.Add(new MidiMeasure());
        
        // Traverse factor oracle until time runs out
        var rng = new Random();
        var time = 0.0;
        var index = rng.Next(HumanFourStart, NoteFactorOracle.Nodes.Count - 1);
        
        while (time < generateMeasureCount)
        {
            // Traverse
            var (note, newIndex) = NoteFactorOracle.Nodes[index].Traverse(index, rng);

            // Go back to start if finished
            if (note == null || newIndex >= NoteFactorOracle.Nodes.Count)
                (note, newIndex) = NoteFactorOracle.Nodes[0].Traverse(0, rng);
            
            // Add note to measure
            var measureNum = (int)Math.Truncate(time);
            var measure = measures[measureNum];

            var absoluteNote = _leadSheet.ChordAtTime(startMeasureNum + time).V1_GetAbsoluteNote(note.Note);
            var newNote = new MidiNote(OutputName.Algorithm, time - measureNum, note.Length, absoluteNote, note.Velocity);
            measure.Notes.Add(newNote);
            
            // Iterate
            index = newIndex;

            // Increase time, quantize
            var newTime = time + note.Length + note.RestLength;
            newTime *= 4;
                
            var truncNewTime = (int)Math.Truncate(newTime);
            var fracNewTime = newTime - truncNewTime;

            fracNewTime = _leadSheet.Style switch
            {
                LeadSheet.StyleEnum.Swing => fracNewTime switch
                {
                    < 0.333 => 0.0,
                    < 0.833 => 0.666,
                    _ => 1.0
                },
                LeadSheet.StyleEnum.Straight => fracNewTime switch
                {
                    < 0.25 => 0.0,
                    < 0.75 => 0.5,
                    _ => 1.0
                },
                _ => throw new ArgumentOutOfRangeException()
            };

            newTime = truncNewTime + fracNewTime;
            newTime /= 4;

            time = newTime;
        }

        return measures;
    }
}