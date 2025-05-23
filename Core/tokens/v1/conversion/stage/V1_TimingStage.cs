﻿using System.Diagnostics;
using Core.midi;
using static Core.tokens.v1.conversion.V1_TokenMelody;

namespace Core.tokens.v1.conversion.stage;

public static class V1_TimingStage
{
    private abstract record TimingStageUnit(double Time, double Length);
    private record TimingStageUnitToken(V1_TokenMelodyToken Token) : TimingStageUnit(Token.Time, Token.Length);
    private record TimingStageUnitRest(double Time, double Length) : TimingStageUnit(Time, Length);

    public static V1_TimedTokenMelody TokenizeTiming(V1_TokenMelody tokenMelody, LeadSheet? leadSheet)
    {
        var tokens = tokenMelody.Tokens;
        
        // Lengthen sequence per measure
        List<List<TimingStageUnit>> units = GetUnits(tokens, leadSheet);

        if (units is [])
            return new V1_TimedTokenMelody();
        
        // Fit measure to result
        return Fit(units);
    }

    private static List<List<TimingStageUnit>> GetUnits(List<V1_TokenMelodyToken> tokens, LeadSheet? leadSheet)
    {
        // Early return
        if (tokens is [])
            return [];
        
        // Move notes that are close to barlines
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var tokenTime = token.Time;
            var tokenSnapped = Math.Round(token.Time);
            
            if (tokenTime - Math.Truncate(tokenTime) is > 0.997 or < 0.003)
                tokens[index] = token with { Time = tokenSnapped };
        }

        // Delete small notes
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!(tokens[index].Length < 0.03)) continue;

            // Console.WriteLine("Small note removed");
            tokens.RemoveAt(index);
            index -= 1;
        }
        
        // Early return
        if (tokens is [])
            return [];
        
        // Delete small rests, make monophonic
        var measureCount = (int)Math.Truncate(tokens.Max(it => it.Time)) + 1;
        var measures = Enumerable.Range(0, measureCount)
            .Select(it => tokens.Where(t => (int)Math.Truncate(t.Time) == it).ToList());

        List<List<TimingStageUnit>> res = [];

        foreach (var measure in measures)
        {
            List<TimingStageUnit> resMeasure = [];
            
            // Add dummy note to end of measure
            measure.Add(new V1_TokenMelodyNote(0, 1.0, 0.0, 0));
            
            // Handle initial / full-measure rest
            resMeasure.Add(new TimingStageUnitRest(0.0, measure is [] ? 1.0 : measure[0].Time));
            
            // Handle all but dummy note
            for (var index = 0; index < measure.Count - 1; index++)
            {
                var token = measure[index];
                var nextToken = measure[index + 1];

                var addRest = true;
                
                var restLength = nextToken.Time - (token.Time + token.Length);
                if (restLength < 0.6 * token.Time)
                {
                    measure[index] = token with { Length = nextToken.Time - token.Time };
                    addRest = false;
                }
            
                // Remove swing
                var swing = (leadSheet?.Style ?? LeadSheet.StyleEnum.Straight) == LeadSheet.StyleEnum.Swing;
                var newTime = RemoveSwing(token.Time, swing);
                var newLength = token.Length - (newTime - token.Time);
                
                // Add token (and maybe rest) to measure
                resMeasure.Add(new TimingStageUnitToken(token with { Time = newTime, Length = newLength }));
                if (addRest) resMeasure.Add(new TimingStageUnitRest(token.Time + token.Length, restLength));
            }
            
            res.Add(resMeasure);
        }

        return res;
    }
    
    private static V1_TimedTokenMelody Fit(List<List<TimingStageUnit>> units)
    {
        // Initialize result
        var res = new List<V1_TimedTokenMelody.V1_TimedTokenMelodyToken>(units.Count);
        
        // Fit to sections
        foreach (var measure in units)
        {
            var best = double.PositiveInfinity;
            var startIndex = 0;
            var phraseStart = 0.0;
            var previousPhraseEnd = 0.0;
            var previousAverageSpeed = V1_TokenMethods.V1_TokenSpeed.Fast;

            for (var index = 0; index < measure.Count; index++)
            {
                var unit = measure[index];

                var currentPhrase = measure.Skip(startIndex).Take(index - startIndex + 1).ToArray();
                
                // Get average speed of phrase
                var averageSpeed = currentPhrase.Average(it => it.Time).V1_ToSpeed();
                var averageSpeedDouble = averageSpeed.V1_ToDouble();
                
                // Quantize end of unit (based on average speed)
                var phraseEnd = (int)Math.Ceiling((unit.Time + unit.Length) / averageSpeedDouble) * averageSpeedDouble;
                var unitTime = (phraseEnd - phraseStart) / currentPhrase.Length;
                
                // Get average deviation of end of units
                var averageError = currentPhrase.Select(
                    (phraseUnit, i) => Math.Abs(phraseUnit.Time + phraseUnit.Length - (phraseStart + i * unitTime))
                ).Average();
            
                // Check if unit does not improve average error
                if (averageError > best || index == measure.Count - 1)
                {
                    if (index == measure.Count - 1)
                    {
                        index += 1;
                        previousAverageSpeed = averageSpeed;
                        previousPhraseEnd = phraseEnd;
                    }
                    
                    // Add previous average speed
                    res.Add(new V1_TimedTokenMelody.V1_TimedTokenMelodySpeed(previousAverageSpeed));

                    // Add all in [startIndex..(index-1)] to result
                    for (var j = startIndex; j < index; j++)
                    {
                        var addUnit = measure[j];
                        if (addUnit is TimingStageUnitToken(V1_TokenMelodyNote(var nScaleTone, _, _, var nVelocity)))
                            res.Add(new V1_TimedTokenMelody.V1_TimedTokenMelodyNote(nScaleTone, nVelocity));
                        else if (addUnit is TimingStageUnitToken(V1_TokenMelodyPassingTone(_, _, var ptVelocity)))
                            res.Add(new V1_TimedTokenMelody.V1_TimedTokenMelodyPassingTone(ptVelocity));
                        else if (addUnit is TimingStageUnitRest)
                            res.Add(new V1_TimedTokenMelody.V1_TimedTokenMelodyRest());
                    }

                    // Reset variables
                    best = double.PositiveInfinity;
                    phraseStart = previousPhraseEnd;
                    previousPhraseEnd = 0.0;
                    previousAverageSpeed = V1_TokenMethods.V1_TokenSpeed.Fast;

                    // Restart at current index
                    index -= 1;
                }
                else
                {
                    best = averageError;
                    previousPhraseEnd = phraseStart;
                    previousAverageSpeed = averageSpeed;
                }
            }
            
            res.Add(new V1_TimedTokenMelody.V1_TimedTokenMelodyMeasure());
        }

        return new V1_TimedTokenMelody { Tokens = res };
    }

    public static double RemoveSwing(double time, bool enable)
    {
        // Early return
        if (!enable) return time;
        
        // Truncate
        var trunc = (int)Math.Truncate(time);
        time -= trunc;

        if (time < 0.666)
            time = time / 0.666 * 0.5;
        else
            time = 1 - (1 - time) / 0.333 * 0.5;

        time += trunc;

        return time;
    }

    public static double ApplySwing(double time, bool enable)
    {
        // Early return
        if (!enable) return time;
        
        // Truncate
        var trunc = (int)Math.Truncate(time);
        time -= trunc;

        Debug.Assert(time is >= 0.0 and <= 1.0);

        if (time < 0.5)
            time = time / 0.5 * 0.66;
        else
            time = 1 - ((1 - time) / 0.5 * 0.33);

        time += trunc;

        return time;
    }
    
    private record Phrase(List<V1_TokenMelodyToken> Tokens, double Start, double End, V1_TokenMethods.V1_TokenSpeed FinalSpeed);
    public static V1_TokenMelody ReconstructTiming(V1_TimedTokenMelody timedTokenMelody, LeadSheet leadSheet)
    {
        var tokens = timedTokenMelody.Tokens;

        var res = new V1_TokenMelody();
        
        List<Phrase> currentMeasure = [];
        var currentPhrase = new Phrase([], 0.0, -1, V1_TokenMethods.V1_TokenSpeed.Fast);
        var currentSpeed = V1_TokenMethods.V1_TokenSpeed.Fast;
        var currentMeasureTime = 0.0;
        var currentMeasureNum = 0;
        foreach (var token in tokens)
        {
            // Handle ending of phrase
            if (token is V1_TimedTokenMelody.V1_TimedTokenMelodyMeasure or V1_TimedTokenMelody.V1_TimedTokenMelodySpeed)
            {
                currentMeasure.Add(currentPhrase with { End = currentMeasureTime });
                currentPhrase = new Phrase([], currentMeasureTime, -1, currentSpeed);
            }

            var tokenTime = currentSpeed.V1_ToDouble();
            
            // Handle measure
            if (token is V1_TimedTokenMelody.V1_TimedTokenMelodyMeasure)
            {
                // Handle empty measure
                if (currentMeasure is [{ Tokens: [] }])
                {
                    currentMeasureNum++;
                    currentMeasureTime = 0.0;
                    currentMeasure = [];
                    continue;
                }

                // Get length of all measures
                var measureLength = currentMeasure[^1].End;
                
                // Handle all phrases
                for (var index = 0; index < currentMeasure.Count; index++)
                {
                    var phrase = currentMeasure[index];

                    var finalSpeedDouble = phrase.FinalSpeed.V1_ToDouble();

                    // Scale phrases such that measure is of length 1,
                    // Adjust start of phrase, snap end of phrase
                    phrase = phrase with
                    {
                        Start = index == 0 ? phrase.Start / measureLength : currentMeasure[index - 1].End,
                        // End = phrase.End / measureLength
                        End = (int)Math.Ceiling(phrase.End / measureLength / finalSpeedDouble) * finalSpeedDouble,
                    };

                    // Ignore notes when phrase does not snap correctly
                    if (phrase.End <= phrase.Start)
                    {
                        phrase = phrase with { End = phrase.Start };
                        currentMeasure[index] = phrase;
                        continue;
                    }
                    
                    // Get time per token
                    var newTokenTime = (phrase.End - phrase.Start) / phrase.Tokens.Count;

                    // Add all time-shifted tokens
                    for (var i = 0; i < phrase.Tokens.Count; i++)
                    {
                        var phraseToken = phrase.Tokens[i];

                        // Ignore dummy notes, fix length
                        if (phraseToken is not V1_TokenMelodyNote(_, _, _, 0))
                        {
                            var newTime = phrase.Start + i * newTokenTime;
                            var newLength = newTokenTime;

                            // Apply swing
                            var swing = leadSheet.Style == LeadSheet.StyleEnum.Swing;
                            var oldTime = newTime;
                            newTime = ApplySwing(newTime, swing);
                            newLength = ApplySwing(oldTime + newLength, swing) - newTime;
                            
                            res.Tokens.Add(
                                phraseToken with
                                {
                                    Time = currentMeasureNum + newTime,
                                    Length = newLength
                                }
                            );
                        }
                    }

                    currentMeasure[index] = phrase;
                }

                // Increment time and measure
                currentMeasureNum++;
                currentMeasureTime = 0;
                currentMeasure = [];
                currentPhrase = new Phrase([], 0.0, -1, currentSpeed);
            }
            
            // Handle rest
            else if (token is V1_TimedTokenMelody.V1_TimedTokenMelodyRest)
            {
                // Add dummy note
                var newToken = new V1_TokenMelodyNote(0, currentMeasureTime, tokenTime, 0);
                currentPhrase.Tokens.Add(newToken);
                
                currentMeasureTime += tokenTime;
            }
            
            // Handle note
            else if (token is V1_TimedTokenMelody.V1_TimedTokenMelodyNote(var scaleNote, var nVelocity))
            {
                // Add TokenMelody token to phrase
                var newToken = new V1_TokenMelodyNote(scaleNote, currentMeasureTime, tokenTime, nVelocity);
                currentPhrase.Tokens.Add(newToken);

                currentMeasureTime += tokenTime;
            }
            
            // Handle passing tone
            else if (token is V1_TimedTokenMelody.V1_TimedTokenMelodyPassingTone(var ptVelocity))
            {
                // Add TokenMelody token to phrase
                var newToken = new V1_TokenMelodyPassingTone(currentMeasureTime, tokenTime, ptVelocity);
                currentPhrase.Tokens.Add(newToken);
                
                currentMeasureTime += tokenTime;
            }
            
            // Handle speed
            else if (token is V1_TimedTokenMelody.V1_TimedTokenMelodySpeed(var speed))
            {
                currentSpeed = speed;
            }
        }
        
        // Return
        return res;
    }
}
