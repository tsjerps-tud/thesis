using System;
using System.Collections.Generic;
using System.Threading;
using Core.midi;
using Core.midi.token;
using Godot;
using Program.midi.recorder;
using Program.midi.scheduler.component;
using Program.midi.scheduler.component.solo;
using Program.screen.performance;
using Program.util;
using static Godot.FileAccess.ModeFlags;

namespace Program.midi.scheduler;

public partial class MidiScheduler : Node
{
	[Export] public MidiRecorder Recorder;
	
	// ReSharper disable once InconsistentNaming
	public double BPM;

	// Integer part: measure
	// Fractional part: part within measure
	public double Time = -2.0;
	
	public PriorityQueue<MidiNote, double> NoteQueue = new();

	public DateTime StartDateTime;
	public int SongLength;
	public int Repetitions;
	
	public bool Enabled;

	public List<MidiSchedulerComponent> Components = new();
	
	public void InitializePerformance()
	{
		// Get path of standard
		var init = GetNode("/root/PerformanceScreenInit");
		var standardName = (string)init.Get("standard_name");
		var standardPath = $"user://saves/{standardName}/";

		var soloistIndex = (int)init.Get("soloist");
		
		// Load necessary files
		var backingTrack = MidiSong.FromNotesFileStream(new FileAccessStream(standardPath + "backing.notes", Read));
		var soloTrack = MidiSong.FromNotesFileStream(new FileAccessStream(standardPath + "solo.notes", Read));
		var leadSheet = LeadSheet.FromStream(new FileAccessStream(standardPath + "sheet.json", Read));
		
		// Get BPM
		BPM = leadSheet.BPM;
		
		// Add components
		SongLength = backingTrack.Measures.Count;
		Repetitions = (int)init.Get("repetition_count");
		Components.Add(new SongMidiSchedulerComponent
		{
			Scheduler = this,
			Recorder = Recorder,
			Song = backingTrack,
			Repetitions = Repetitions,
		});

		Soloist soloist = soloistIndex switch
		{
			0 => new NoteRandomSoloist(),
			1 => new RetrievalSoloist(),
			2 => new NoteFactorOracleSoloist(),
			3 => new TokenRandomSoloist(),
			4 => new TokenFactorOracleSoloist(), 
			5 => new TokenMarkovSoloist(standardPath),
			6 => new TokenShuffleSoloist(),
			7 => new TokenTransformerSoloist(),
			_ => throw new ArgumentOutOfRangeException()
		};
        
		Components.Add(new SoloMidiSchedulerComponent(soloTrack, leadSheet, soloist)
		{
			Scheduler = this,
			Recorder = Recorder,
			Repetitions = Repetitions
		});

		AddMeasure(-2, new MidiMeasure([
			new MidiNote(OutputName.Metronome, 0.0, 0.2, 22, 48),
			new MidiNote(OutputName.Metronome, 0.5, 0.2, 22, 48),
		]));
		
		AddMeasure(-1, new MidiMeasure([
			new MidiNote(OutputName.Metronome, 0.00, 0.2, 22, 48),
			new MidiNote(OutputName.Metronome, 0.25, 0.2, 22, 48),
			new MidiNote(OutputName.Metronome, 0.50, 0.2, 22, 48),
			new MidiNote(OutputName.Metronome, 0.75, 0.2, 22, 48),
		]));
	}

	public void Start()
	{
		MidiServer.Instance.Scheduler = this;
		Enabled = true;
		
		var thread = new Thread(Run);
		thread.Start();
	}

	public void Stop()
	{
		MidiServer.Instance.Scheduler = null;
		Enabled = false;

		var currentMeasure = (int)Math.Truncate(Time);
		
		lock (NoteQueue)
			while (NoteQueue.TryDequeue(out var note, out _))
				MidiServer.Instance.Send(note with { Time = note.Time + currentMeasure });
	}
	
	public void Run()
	{
		StartDateTime = DateTime.Now;
		
		while (Enabled)
		{
			var currentTime = GetTime(DateTime.Now);
			Tick(currentTime);
		}	
	}

	public double GetTime(DateTime dateTime)
	{
		var elapsed = dateTime - StartDateTime;
		double currentTimeMs = elapsed.TotalMilliseconds;
		var currentMeasure = currentTimeMs / 1000.0 / (60.0 / BPM) / 4.0;
		
		return currentMeasure - 2;
	}
    
	public void Tick(double currentTime)
	{
		// Call components every measure
		var currentMeasure = (int)Math.Truncate(currentTime);
		if ((int)Math.Truncate(Time) != currentMeasure)
		{
			if (currentMeasure == 1 + SongLength * Repetitions)
			{
				Recorder.CallDeferred("Save");
				((PerformanceScreen)GetTree().CurrentScene).CallDeferred("Quit");
				
				Enabled = false;
				return;
			}
			
			foreach (var component in Components)
				component.HandleMeasure(currentMeasure);
		}
		
		// Update current time
		Time = currentTime;
		
		// Play notes when needed
		lock (NoteQueue)
		{
			while (NoteQueue.TryPeek(out _, out var time) && time < Time)
			{
				var note = NoteQueue.Dequeue();
				MidiServer.Instance.Send(note with { Time = note.Time + currentMeasure });
			}
		}
	}

	public void AddMeasure(int measureNum, MidiMeasure measure)
	{
		lock (NoteQueue)
		{
			foreach (var note in measure.Notes)
			{
				NoteQueue.Enqueue(note, note.Time + measureNum);

				// Add note off
				if (note.Length == 0.0) continue;
				if (note.Velocity <= 0) continue;
				var noteOffTime = note.Time + note.Length;
				NoteQueue.Enqueue(new MidiNote(note.OutputName, noteOffTime, 0, note.Note, 0), noteOffTime + measureNum);
			}
		}
	}

	public void AddSong(int startMeasureNum, MidiSong song)
	{
		lock (NoteQueue)
		{
			for (var index = 0; index < song.Measures.Count; index++)
				AddMeasure(startMeasureNum + index, song.Measures[index]);
		}
	}
}