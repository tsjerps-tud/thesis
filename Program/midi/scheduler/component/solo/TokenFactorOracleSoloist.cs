﻿using System;
using System.Collections.Generic;
using System.Linq;
using Core.conversion;
using Core.midi;
using Core.midi.token;
using Godot;

namespace Program.midi.scheduler.component.solo;

public class TokenFactorOracleSoloist : Soloist
{
    public TokenFactorOracle TokenFactorOracle;
    public LeadSheet LeadSheet;

    public int HumanFourStart;
    
    public override void Initialize(MidiSong solo, LeadSheet leadSheet)
    {
        TokenFactorOracle = new();
        
        IngestMeasures(solo.Measures, 0);
        
        LeadSheet = leadSheet;
    }

    public override void IngestMeasures(List<MidiMeasure> measures, int startMeasureNum)
    {
        HumanFourStart = TokenFactorOracle.Nodes.Count;
        
        // Get notes (shifted by measure number)
        List<MidiNote> notes = [];
        for (var index = 0; index < measures.Count; index++)
            foreach (var note in measures[index].Notes)
                notes.Add(note with { Time = note.Time + index });
        
        // Get tokens, add to factor oracle
        var tokens = Conversion.Tokenize(notes, LeadSheet);
        TokenFactorOracle.AddTokens(tokens);
    }

    public override List<MidiMeasure> Generate(int generateMeasureCount, int startMeasureNum)
    {
        List<Token> res = [];
        
        // Traverse factor oracle
        var rng = new Random();
        var index = rng.Next(HumanFourStart, TokenFactorOracle.Nodes.Count - 1);
        
        var measureCount = 0;
        var tokenCount = 0;
        while (true)
        {
            // Traverse
            var (newToken, newIndex) = TokenFactorOracle.Nodes[index].Traverse(index, rng);

            if (newIndex == -1)
                (newToken, newIndex) = TokenFactorOracle.Nodes[0].Traverse(0, rng);
            
            // Generate token, add
            res.Add(newToken);
            tokenCount++;

            var measureTooLong = tokenCount > 10 && newToken != Token.Measure;
            
            if (measureTooLong)
            {
                // Force measure token
                res.Add(Token.Measure);
            }
            
            if (newToken == Token.Measure || measureTooLong)
            {
                // Advance measure
                measureCount++;
                tokenCount = 0;

                // Break if measure count reached
                if (measureCount == generateMeasureCount)
                    break;
            }
            
            // Iterate
            index = newIndex;
        }
        
        while (res is [Token.Measure, ..])
            res.RemoveAt(0);
        
        // Reconstruct, return notes
        var notes = Conversion.Reconstruct(res, LeadSheet, startMeasureNum);
        return MidiSong.FromNotes(notes).Measures;
    }
}