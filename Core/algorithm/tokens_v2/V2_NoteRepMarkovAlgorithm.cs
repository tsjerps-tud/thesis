﻿using Core.midi;
using Core.models.tokens_v2;
using Core.tokens.v2.conversion.stage;

namespace Core.algorithm.tokens_v2;

public record NoteRepresentation(
    int Note, // [0, 24]
    int Velocity, // [0, 1]
    int Ioi // [0, 3]
);

public class V2_NoteRepMarkovAlgorithm: IAlgorithm
{
    public List<NoteRepresentation> RepsFromNotes(List<MidiNote> notes)
    {
        // Optionally remove swing
        notes = notes
            .Select(it => it with
            {
                Time = V2_TimingStage.RemoveSwing(it.Time, LeadSheet.Style == LeadSheet.StyleEnum.Swing)
            })
            .ToList();

        List<NoteRepresentation> res = [];
        
        for (var i = 0; i < notes.Count; i++)
        {
            var (_, time, _, nNote, nVelocity) = notes[i];

            time = V2_TimingStage.RemoveSwing(time, LeadSheet.Style == LeadSheet.StyleEnum.Swing);

            int nIoi;
            if (i != notes.Count - 1)
                nIoi = (int)((notes[i + 1].Time - time) * 8);
            else
                nIoi = 2;
            if (nIoi > 3) nIoi = 3;
            if (nIoi < 0) nIoi = 0;
            
            nNote %= 24;
            nVelocity /= 64;

            var rep = new NoteRepresentation(
                nNote,
                nVelocity,
                nIoi
            );
            
            res.Add(rep);
        }

        return res;
    }

    public List<MidiNote> NotesFromReps(List<NoteRepresentation> reps, int generateMeasureCount)
    {
        List<MidiNote> res = [];

        var t = 0.0;
        foreach (var rep in reps)
        {
            var (note, velocity, ioi) = rep;

            note += 60;

            var fIoi = ioi / 8.0;

            var swungTime = t;
            var swungNextTime = t + fIoi;

            swungTime = V2_TimingStage.ApplySwing(swungTime, LeadSheet.Style == LeadSheet.StyleEnum.Swing);
            swungNextTime = V2_TimingStage.ApplySwing(swungNextTime, LeadSheet.Style == LeadSheet.StyleEnum.Swing);
            
            var newNote = new MidiNote(
                OutputName.Algorithm,
                swungTime,
                swungNextTime - swungTime,
                note,
                velocity * 64 + 32
            );
            
            res.Add(newNote);
            t += fIoi;

            if (t > generateMeasureCount)
                break;
        }
        
        return res;
    }

    public GenericContinuator<NoteRepresentation> Model = new(
        it => it.GetHashCode(),
        (a, b) => Math.Abs(a.Note - b.Note),
        3
    );

    public LeadSheet LeadSheet;
    
    public void Initialize(MidiSong[] solos, LeadSheet leadSheet)
    {
        LeadSheet = leadSheet;
        
        Learn(solos[0].Measures, 0);
    }

    public void Learn(List<MidiMeasure> measures, int startMeasureNum = 0)
    {
        // Create notes
        List<MidiNote> notes = [];
        for (var index = 0; index < measures.Count; index++)
        {
            var measure = measures[index];
            notes.AddRange(
                measure.Notes.Select(note => note with { Time = note.Time + index })
            );
        }

        var reps = RepsFromNotes(notes);
        Model.LearnSequence(reps);
    }

    public List<MidiMeasure> Generate(int generateMeasureCount = 4, int startMeasureNum = 0)
    {
        // Generate reps
        var reps = Model.Generate(40);

        var notes = NotesFromReps(reps, generateMeasureCount);
        return MidiSong.FromNotes(notes).Measures;
    }
}