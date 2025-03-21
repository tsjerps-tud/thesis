﻿using Core.midi;
using Godot;
using static Core.midi.OutputName;

namespace Program.midi.scheduler.component;

public class MetronomeMidiSchedulerComponent : MidiSchedulerComponent
{
    public override void HandleMeasure(int currentMeasure)
    {
        Scheduler.AddMeasure(measureNum: currentMeasure, new MidiMeasure([
            new MidiNote(Metronome, Time: 0.00, Length: 0.125, Note: 22, Velocity: 70),
            new MidiNote(Metronome, Time: 0.25, Length: 0.125, Note: 22, Velocity: 30),
            new MidiNote(Metronome, Time: 0.50, Length: 0.125, Note: 22, Velocity: 30),
            new MidiNote(Metronome, Time: 0.75, Length: 0.125, Note: 22, Velocity: 30)
        ]));
    }
}