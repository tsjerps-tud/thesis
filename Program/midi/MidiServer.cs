﻿using System;
using System.Collections.Generic;
using Core.midi;
using Godot;
using NAudio.Midi;
using Program.midi.scheduler;

namespace Program.midi;

public partial class MidiServer : Node
{
    public static MidiServer Instance;

    public MidiScheduler Scheduler;
    
    public MidiIn Input;

    public Dictionary<OutputName, MidiOut> Outputs;

    public delegate void NoteSentHandler(MidiNote note);
    
    public event NoteSentHandler NoteSent;

    [Signal]
    public delegate void DeferredNoteSentEventHandler(int outputName, int note, int velocity);
    
    public override void _Ready()
    {
        Instance = this;

        var keyboardMidiName = (string)GetNode("/root/Config").Get("keyboard_midi_name");
        try
        {
            Input = FindMidiIn(keyboardMidiName);
        }
        catch (Exception)
        {
            // Default to no input
            Input = FindMidiIn("no_input");
            GetNode("/root/Config").Set("keyboard_midi_name", "no_input");
        }
        Input.Start();
        
        Outputs = new()
        {
            [OutputName.Loopback] = FindMidiOut("Loopback"),
            [OutputName.Algorithm] = FindMidiOut("Algorithm"),
            [OutputName.Metronome] = FindMidiOut("Metronome"),
            [OutputName.Backing1Bass] = FindMidiOut("Backing 1 - Bass"),
            [OutputName.Backing2Piano] = FindMidiOut("Backing 2 - Piano"),
            [OutputName.Backing3Keyboard] = FindMidiOut("Backing 3 - Keyboard"),
            [OutputName.Backing4Drums] = FindMidiOut("Backing 4 - Drums"),
        };

        // Hot-fix Unknown output name
        Outputs[OutputName.Unknown] = Outputs[OutputName.Loopback];

        Console.WriteLine($"[MIDI] Setup successful (using keyboard '{keyboardMidiName}')!");

        Input.MessageReceived += (_, args) =>
        {
            if (args.MidiEvent is not NoteEvent noteEvent) return;

            // Workaround for keyboards with different note off velocity
            var velocity = noteEvent.Velocity;
            if (MidiEvent.IsNoteOff(noteEvent)) velocity = 0;
            
            Send(OutputName.Loopback, noteEvent.NoteNumber, velocity);
        };
    }

    public void Send(MidiNote note)
    {
        var noteEvent = new NoteOnEvent(0, 1, note.Note, note.Velocity, 0);
        Outputs[note.OutputName].Send(noteEvent.GetAsShortMessage());

        NoteSent!(note);
        CallDeferred("SendDeferred", (int)note.OutputName, note.Note, note.Velocity);
    }

    public void SendDeferred(int outputName, int note, int velocity)
    {
        EmitSignal(SignalName.DeferredNoteSent, outputName, note, velocity);
    }
    
    public void Send(OutputName outputName, int note, int velocity) =>
        Send(new MidiNote(outputName, Scheduler?.Time ?? 0.0, 0.0, note, velocity));
    
    private MidiOut FindMidiOut(string name)
    {
        for (var i = 0; i < MidiOut.NumberOfDevices; i++)
            if (MidiOut.DeviceInfo(i).ProductName == name)
                return new MidiOut(i);

        throw new ArgumentException($"Cannot find MIDI output {name}");
    }

    private MidiIn FindMidiIn(string name)
    {
        for (var i = 0; i < MidiIn.NumberOfDevices; i++)
            if (MidiIn.DeviceInfo(i).ProductName == name)
                return new MidiIn(i);

        throw new ArgumentException($"Cannot find MIDI input {name}");
    }
}
