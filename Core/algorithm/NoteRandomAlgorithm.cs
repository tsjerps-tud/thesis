﻿using Core.midi;
using Core.tokens.v1;

namespace Core.algorithm;

public class NoteRandomAlgorithm : IAlgorithm
{
    private LeadSheet _leadSheet;
    
    public void Initialize(MidiSong[] solos, LeadSheet leadSheet)
    {
        _leadSheet = leadSheet;
    }

    public void Learn(List<MidiMeasure> measures, int startMeasureNum = 0) { }

    public List<MidiMeasure> Generate(int generateMeasureCount = 4, int startMeasureNum = 0)
    {
        // Create new measures
        var measures = new List<MidiMeasure>();
        for (var i = 0; i < generateMeasureCount; i++)
            measures.Add(new MidiMeasure());
        
        // Traverse factor oracle until time runs out
        var rng = new Random();
        var time = 0.0;
        
        while (time < generateMeasureCount)
        {
            // Generate
            var note = rng.Next(40, 50);
            var length = rng.NextDouble() * 0.2 + 0.125;
            var restLength = rng.NextDouble() * 0.25;
            var velocity = 96;
            
            // Add note to measure
            var measureNum = (int)Math.Truncate(time);
            var measure = measures[measureNum];

            var absoluteNote = _leadSheet.ChordAtTime(startMeasureNum + time).V1_GetAbsoluteNote(note);
            var newNote = new MidiNote(OutputName.Algorithm, time - measureNum, length, absoluteNote, velocity);
            measure.Notes.Add(newNote);
            
            // Increase time, quantize
            var newTime = time + length + restLength;
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