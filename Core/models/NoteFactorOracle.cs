﻿using Core.midi;

namespace Core.models;

public class NoteFactorOracle
{
    public class Node
    {
        // Key: note, value: index of node
        private readonly Dictionary<MidiMelody.MelodyNote, int> _transitions = new();

        public int Supply;
            
        public int this[MidiMelody.MelodyNote index]
        {
            get => _transitions[index];
            set => _transitions[index] = value;
        }

        public int this[int note] => _transitions[_transitions.Keys.First(it => it.Note == note)];

        public bool Has(int note) => _transitions.Keys.Any(key => key.Note == note);

        public (MidiMelody.MelodyNote?, int) Traverse(int currentIndex, Random rng)
        {
            if (_transitions.Count == 0)
                return (null, -1);

            // Increase odds of continuing
            if (rng.NextDouble() < 0.5)
            {
                using var newKvps = _transitions
                    .Where(tuple => tuple.Value == currentIndex + 1)
                    .GetEnumerator();

                return newKvps.MoveNext() ? (newKvps.Current.Key, newKvps.Current.Value) : (null, -1);
            }
                
            // Get random note from transitions, return it with next index
            var key = _transitions.Keys.ElementAt(rng.Next(_transitions.Keys.Count));
            return (key, _transitions[key]);
        }
    }

    public List<Node> Nodes = [];
        
    public void AddNote(MidiMelody.MelodyNote symbol)
    {
        // Get note
        var note = symbol.Note;
            
        // Create a new state
        var newNode = new Node();
            
        // Add the node if it is the first
        if (Nodes is not [.., var lastNode])
        {
            newNode.Supply = -1;
            Nodes.Add(newNode);
            return;
        }

        // Create a new transition from m to m + 1 labeled by note
        lastNode[symbol] = Nodes.Count;

        var k = lastNode.Supply;
        while (k > -1 && !Nodes[k].Has(note))
        {
            // Create a new transition from k to m + 1 by sigma
            Nodes[k][symbol] = Nodes.Count;
                
            k = Nodes[k].Supply;
        }

        newNode.Supply = k == -1 ? 0 : Nodes[k][note];
        Nodes.Add(newNode);
    }

    public void AddMelody(MidiMelody melody)
    {
        foreach (var note in melody.Melody)
            AddNote(note);
    }
}