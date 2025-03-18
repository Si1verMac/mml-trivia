using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TriviaApp.Data;
using TriviaApp.Hubs;
using TriviaApp.Models;

namespace TriviaApp.Services
{
    public class AnswerService
    {
        private readonly TriviaDbContext _context;
        private readonly ILogger<AnswerService> _logger;
        private readonly IHubContext<TriviaHub> _hubContext;

        // Track Lightning Bonus submissions to determine order
        private static readonly Dictionary<int, List<(int teamId, DateTime submittedAt, bool isCorrect)>> _lightningBonusSubmissions = new();

        public AnswerService(TriviaDbContext context, IHubContext<TriviaHub> hubContext, ILogger<AnswerService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _hubContext = hubContext;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(bool isCorrect, bool allSubmitted, int scoreChange)> SubmitAnswerAsync(int gameId, int teamId, int questionId, string selectedAnswer, int? wager)
        {
            _logger.LogInformation("Received answer submission: GameId={GameId}, TeamId={TeamId}, QuestionId={QuestionId}, SelectedAnswer={SelectedAnswer}, Wager={Wager}",
                gameId, teamId, questionId, selectedAnswer, wager);

            var question = await _context.Questions.FindAsync(questionId);
            if (question == null)
            {
                throw new Exception("Question not found");
            }

            var game = await _context.Games
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null)
            {
                throw new Exception("Game not found");
            }

            // Use the question's Type from the database instead of round-based logic
            string questionType = (question.Type?.ToLowerInvariant() ?? "regular").Trim('"'); ;
            _logger.LogInformation("QUESTTYPE DEBUG - Question type from database: '{QuestionType}', raw value: '{RawType}'",
                questionType, question.Type);

            // Special checks for halftime bonus (to catch all variations)
            if (questionType.Contains("halftime") || questionType.Contains("half-time") || questionType.Contains("half time"))
            {
                _logger.LogInformation("QUESTTYPE DEBUG - Detected halftime bonus question through text checking");
                questionType = "halftimebonus";
            }

            // Log the correct answer and selected answer for debugging
            LogAnswerDetails(question, selectedAnswer);

            bool isCorrect = false;
            int scoreChange = 0;

            // Handle different question types with appropriate scoring
            switch (questionType)
            {

                case "regular":
                case "finalwager":
                    // Regular question or final wager - simple correct/incorrect with wager
                    isCorrect = IsRegularAnswerCorrect(selectedAnswer, question.CorrectAnswer);
                    scoreChange = isCorrect ? wager ?? 0 : -(wager ?? 0); // Add wager if correct, subtract if incorrect
                    break;

                case "lightning":
                    // Lightning Bonus - First correct gets +5, second gets +3, others get +1, no negative points
                    isCorrect = IsRegularAnswerCorrect(selectedAnswer, question.CorrectAnswer);

                    if (isCorrect)
                    {
                        lock (_lightningBonusSubmissions)
                        {
                            if (!_lightningBonusSubmissions.ContainsKey(questionId))
                            {
                                _lightningBonusSubmissions[questionId] = new List<(int, DateTime, bool)>();
                            }

                            if (!_lightningBonusSubmissions[questionId].Any(s => s.teamId == teamId))
                            {
                                _lightningBonusSubmissions[questionId].Add((teamId, DateTime.UtcNow, isCorrect));

                                if (_lightningBonusSubmissions[questionId].Count == 1)
                                {
                                    scoreChange = 5; // First correct answer
                                }
                                else if (_lightningBonusSubmissions[questionId].Count == 2)
                                {
                                    scoreChange = 3; // Second correct answer
                                }
                                else
                                {
                                    scoreChange = 1; // All other correct answers
                                }
                            }
                        }
                    }
                    else
                    {
                        scoreChange = 0; // No negative points for incorrect answers
                    }
                    break;

                case "halftimebonus":
                case "halftime":
                case "halftime bonus":
                case "halftime_bonus":
                    // Halftime Bonus - Multiple correct answers possible
                    _logger.LogInformation("QUESTTYPE DEBUG - Executing halftime bonus case");
                    List<string> submittedAnswers = ParseAnswerList(selectedAnswer);
                    _logger.LogInformation("HALFTIME DEBUG - Parsed submitted answers: {@SubmittedAnswers}", submittedAnswers);

                    List<string> correctAnswers = GetCorrectAnswersList(question.CorrectAnswer);
                    _logger.LogInformation("HALFTIME DEBUG - Parsed correct answers: {@CorrectAnswers}", correctAnswers);

                    int correctCount = CountCorrectHalftimeAnswers(submittedAnswers, correctAnswers);
                    _logger.LogInformation("HALFTIME DEBUG - Correct answer count: {CorrectCount}", correctCount);

                    scoreChange = correctCount;
                    if (correctCount >= 8)
                    {
                        scoreChange += 1; // +1 bonus for all 8 correct
                    }
                    isCorrect = correctCount > 0;
                    break;

                case "multiQuestion":
                case "multquestion":
                case "multi question":
                case "multiquestion":
                    if (question.CorrectAnswer != null && question.CorrectAnswer.Length > 0)
                    {
                        try
                        {
                            // Log raw database answer
                            _logger.LogInformation("MULTIQUESTION DEBUG - Raw CorrectAnswer from database: {@CorrectAnswer}", question.CorrectAnswer);

                            // Check if EF Core returned the array as a single curly-braced string
                            string[] correctAnswerArray;

                            if (question.CorrectAnswer.Length == 1 && question.CorrectAnswer[0].Contains(','))
                            {
                                // Single curly-braced string returned; split manually
                                correctAnswerArray = question.CorrectAnswer[0]
                                    .Trim('{', '}')
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                    .Select(ans => ans.ToLowerInvariant())
                                    .ToArray();
                            }
                            else
                            {
                                // Already properly parsed array
                                correctAnswerArray = question.CorrectAnswer
                                    .Select(ans => ans.Trim('{', '}').Trim().ToLowerInvariant())
                                    .ToArray();
                            }

                            _logger.LogInformation("MULTIQUESTION DEBUG - Normalized correct answers array: {@CorrectAnswers}", correctAnswerArray);

                            // Parse submitted answers JSON array
                            var submittedAnswersList = JsonConvert.DeserializeObject<List<string>>(selectedAnswer)
                                .Select(ans => ans.Trim('{', '}').Trim().ToLowerInvariant())
                                .ToList();

                            _logger.LogInformation("MULTIQUESTION DEBUG - Parsed submitted answers: {@SubmittedAnswers}", submittedAnswersList);

                            int multiCorrectCount = 0;

                            // Positional comparison, case-insensitive
                            for (int i = 0; i < correctAnswerArray.Length && i < submittedAnswersList.Count; i++)
                            {
                                if (!string.IsNullOrEmpty(submittedAnswersList[i]) &&
                                    submittedAnswersList[i] == correctAnswerArray[i])
                                {
                                    multiCorrectCount++;
                                    _logger.LogInformation("MULTIQUESTION DEBUG - Correct at position {Position}: '{SubmittedAnswer}' matches '{CorrectAnswer}'",
                                        i + 1, submittedAnswersList[i], correctAnswerArray[i]);
                                }
                                else
                                {
                                    _logger.LogInformation("MULTIQUESTION DEBUG - Incorrect at position {Position}: '{SubmittedAnswer}' vs '{CorrectAnswer}'",
                                        i + 1, submittedAnswersList[i], correctAnswerArray[i]);
                                }
                            }

                            scoreChange = multiCorrectCount;
                            isCorrect = multiCorrectCount > 0;

                            _logger.LogInformation("MULTIQUESTION DEBUG - Total correct: {CorrectCount}, Score: {Score}, IsCorrect: {IsCorrect}",
                                multiCorrectCount, scoreChange, isCorrect);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "MULTIQUESTION DEBUG - Exception processing multi-question for team {TeamId}: {Answer}", teamId, selectedAnswer);
                            scoreChange = 0;
                            isCorrect = false;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("MULTIQUESTION DEBUG - CorrectAnswer is null or empty for question {QuestionId}", questionId);
                        scoreChange = 0;
                        isCorrect = false;
                    }
                    break;

                default:
                    _logger.LogWarning("Unknown question type {QuestionType} for QuestionId={QuestionId}, treating as regular",
                        questionType, questionId);
                    isCorrect = IsRegularAnswerCorrect(selectedAnswer, question.CorrectAnswer);
                    scoreChange = isCorrect ? wager ?? 0 : -(wager ?? 0);
                    break;
            }

            // Save or update answer
            var answer = await _context.Answers.FirstOrDefaultAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == questionId);
            if (answer == null)
            {
                answer = new Answer
                {
                    GameId = gameId,
                    TeamId = teamId,
                    QuestionId = questionId,
                    SelectedAnswer = selectedAnswer,
                    Wager = wager,
                    IsCorrect = isCorrect,
                    SubmittedAt = DateTime.UtcNow
                };
                _context.Answers.Add(answer);
            }
            else
            {
                answer.SelectedAnswer = selectedAnswer;
                answer.Wager = wager;
                answer.IsCorrect = isCorrect;
                answer.SubmittedAt = DateTime.UtcNow;
                _context.Answers.Update(answer);
            }

            // Update team score
            var gameTeam = await _context.GameTeams.FirstOrDefaultAsync(gt => gt.GameId == gameId && gt.TeamId == teamId);
            if (gameTeam != null)
            {
                gameTeam.Score += scoreChange;
                _context.GameTeams.Update(gameTeam);
            }

            await _context.SaveChangesAsync();

            int totalActiveTeams = TriviaHub.GetActiveTeamCount(gameId);
            int submittedCount = await _context.Answers
                .Where(a => a.GameId == gameId && a.QuestionId == questionId)
                .Select(a => a.TeamId)
                .Distinct()
                .CountAsync();

            bool allSubmitted = submittedCount >= totalActiveTeams && totalActiveTeams > 0;

            if (allSubmitted)
            {
                var gameQuestion = await _context.GameQuestions
                    .FirstOrDefaultAsync(gq => gq.GameId == gameId && gq.QuestionId == questionId);
                if (gameQuestion != null)
                {
                    gameQuestion.IsAnswered = true;
                    await _context.SaveChangesAsync();
                }
                _logger.LogInformation("All active teams submitted answers for game {GameId}, question {QuestionId}.", gameId, questionId);

                if (questionType == "lightning")
                {
                    lock (_lightningBonusSubmissions)
                    {
                        _lightningBonusSubmissions.Remove(questionId);
                    }
                }
            }

            return (isCorrect, allSubmitted, scoreChange);
        }

        #region Helper Methods

        private void LogAnswerDetails(Question question, string selectedAnswer)
        {
            string correctAnswerDisplay = question.CorrectAnswer != null
                ? (question.CorrectAnswer.Length > 0
                    ? string.Join("; ", question.CorrectAnswer)
                    : "Empty array")
                : "null";

            _logger.LogInformation("Question CorrectAnswer: {CorrectAnswer}", correctAnswerDisplay);
            _logger.LogInformation("Selected Answer: {SelectedAnswer}", selectedAnswer);
        }

        private bool IsRegularAnswerCorrect(string selectedAnswer, string[] correctAnswers)
        {
            if (string.IsNullOrWhiteSpace(selectedAnswer))
                return false;

            if (correctAnswers == null || correctAnswers.Length == 0)
                return false;

            // Use the first answer as the correct one for regular questions
            string correctAnswer = correctAnswers[0];

            return IsStringMatch(selectedAnswer, correctAnswer);
        }

        private bool IsStringMatch(string str1, string str2)
        {
            if (string.IsNullOrWhiteSpace(str1) || string.IsNullOrWhiteSpace(str2))
                return false;

            string trimmed1 = str1.Trim(new Char[] { ' ', '"', '.', '{', '}' });
            string trimmed2 = str2.Trim(new Char[] { ' ', '"', '.', '{', '}' });

            bool isMatch = trimmed1.Equals(trimmed2, StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("HALFTIME DEBUG - String comparison: '{Str1}' vs '{Str2}' = {IsMatch}",
                trimmed1, trimmed2, isMatch);

            return isMatch;
        }

        private List<string> ParseAnswerList(string answer)
        {
            _logger.LogInformation("HALFTIME DEBUG - ParseAnswerList input: {Answer}", answer);

            if (string.IsNullOrWhiteSpace(answer))
                return new List<string>();

            try
            {
                // Check if the answer is a JSON array
                if (answer.StartsWith("[") && answer.EndsWith("]"))
                {
                    _logger.LogInformation("HALFTIME DEBUG - Parsing as JSON array");
                    try
                    {
                        // Try to properly deserialize the JSON array
                        var result = JsonConvert.DeserializeObject<List<string>>(answer);
                        if (result != null && result.Count > 0)
                        {
                            _logger.LogInformation("HALFTIME DEBUG - Successfully parsed JSON array with {Count} items", result.Count);
                            // Clean each answer by removing braces and quotes
                            var cleanedResult = result.Select(a => a.Trim(new Char[] { ' ', '"', '.', '{', '}' })).ToList();
                            _logger.LogInformation("HALFTIME DEBUG - Cleaned JSON array result: {@Result}", cleanedResult);
                            return cleanedResult;
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        _logger.LogWarning("HALFTIME DEBUG - Error deserializing JSON array: {Error}, trying alternate parsing", jsonEx.Message);
                    }

                    // If JSON deserialization failed, try parsing manually
                    try
                    {
                        // Remove the square brackets
                        string content = answer.Substring(1, answer.Length - 2);

                        // Split by commas, respecting quotes
                        List<string> items = new List<string>();
                        bool inQuotes = false;
                        int startPos = 0;

                        for (int i = 0; i < content.Length; i++)
                        {
                            char c = content[i];
                            if (c == '"')
                            {
                                inQuotes = !inQuotes;
                            }
                            else if (c == ',' && !inQuotes)
                            {
                                // Found a comma outside quotes, extract the item
                                string item = content.Substring(startPos, i - startPos).Trim();
                                // Remove quotes if present
                                if (item.StartsWith("\"") && item.EndsWith("\""))
                                {
                                    item = item.Substring(1, item.Length - 2);
                                }
                                items.Add(item);
                                startPos = i + 1;
                            }
                        }

                        // Add the last item
                        if (startPos < content.Length)
                        {
                            string item = content.Substring(startPos).Trim();
                            if (item.StartsWith("\"") && item.EndsWith("\""))
                            {
                                item = item.Substring(1, item.Length - 2);
                            }
                            items.Add(item);
                        }

                        _logger.LogInformation("HALFTIME DEBUG - Manually parsed array: {@Items}", items);
                        return items;
                    }
                    catch (Exception manualEx)
                    {
                        _logger.LogWarning("HALFTIME DEBUG - Error with manual array parsing: {Error}", manualEx.Message);
                    }
                }
                // Handle comma-separated string
                else if (answer.Contains(","))
                {
                    _logger.LogInformation("HALFTIME DEBUG - Parsing as comma-separated string");
                    var result = answer.Split(',').Select(a => a.Trim()).ToList();
                    _logger.LogInformation("HALFTIME DEBUG - Parsed comma result: {@Result}", result);
                    return result;
                }
                // Handle single answer
                else
                {
                    _logger.LogInformation("HALFTIME DEBUG - Parsing as single answer");
                    return new List<string> { answer.Trim() };
                }

                // Fallback to treating the whole string as one answer
                _logger.LogWarning("HALFTIME DEBUG - No parsing method worked, returning raw value as single answer");
                return new List<string> { answer };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse answer list: {Answer}. Error: {Error}", answer, ex.Message);
                return new List<string> { answer.Trim() };
            }
        }

        private List<string> GetCorrectAnswersList(string[] correctAnswers)
        {
            _logger.LogInformation("HALFTIME DEBUG - GetCorrectAnswersList input: {@CorrectAnswers}",
                correctAnswers?.Select(a => a ?? "null").ToArray() ?? new[] { "null array" });

            if (correctAnswers == null || correctAnswers.Length == 0)
                return new List<string>();

            var result = new List<string>();
            foreach (var answer in correctAnswers)
            {
                // Check if this is a comma-separated list in curly braces
                if (answer.StartsWith("{") && answer.EndsWith("}") && answer.Contains(","))
                {
                    _logger.LogInformation("HALFTIME DEBUG - Found comma-separated list in braces: {Answer}", answer);
                    // Extract the content inside the curly braces and split by commas
                    string content = answer.Substring(1, answer.Length - 2);
                    var splitResults = content.Split(',').Select(a => a.Trim()).ToList();
                    _logger.LogInformation("HALFTIME DEBUG - Split into {@SplitResults}", splitResults);
                    result.AddRange(splitResults);
                }
                else
                {
                    // Single answer, just add it
                    _logger.LogInformation("HALFTIME DEBUG - Adding single answer: {Answer}", answer);
                    result.Add(answer.Trim());
                }
            }

            _logger.LogInformation("HALFTIME DEBUG - Final correct answers list: {@Result}", result);
            return result;
        }

        private int CountCorrectHalftimeAnswers(List<string> submittedAnswers, List<string> correctAnswers)
        {
            _logger.LogInformation("HALFTIME DEBUG - Starting comparison of {SubmittedCount} submitted answers against {CorrectCount} correct answers",
                submittedAnswers.Count, correctAnswers.Count);

            int correctCount = 0;

            // Create a copy of correct answers that we can remove from when matched
            List<string> availableCorrectAnswers = new List<string>(correctAnswers);

            foreach (var submittedAnswer in submittedAnswers)
            {
                _logger.LogInformation("HALFTIME DEBUG - Checking submitted answer: '{SubmittedAnswer}'", submittedAnswer);
                bool foundMatch = false;
                string matchedAnswer = null;

                // Find matching answer among still-available correct answers
                foreach (var correctAnswer in availableCorrectAnswers)
                {
                    bool isMatch = IsStringMatch(submittedAnswer, correctAnswer);
                    if (isMatch)
                    {
                        foundMatch = true;
                        correctCount++;
                        matchedAnswer = correctAnswer;
                        _logger.LogInformation("HALFTIME DEBUG - Found match for '{SubmittedAnswer}' with '{CorrectAnswer}'",
                            submittedAnswer, correctAnswer);
                        break;
                    }
                }

                // Remove the matched answer from available answers
                if (foundMatch && matchedAnswer != null)
                {
                    availableCorrectAnswers.Remove(matchedAnswer);
                    _logger.LogInformation("HALFTIME DEBUG - Removed '{MatchedAnswer}' from available answers. {RemainingCount} answers remaining.",
                        matchedAnswer, availableCorrectAnswers.Count);
                }

                if (!foundMatch)
                {
                    _logger.LogInformation("HALFTIME DEBUG - No match found for '{SubmittedAnswer}'", submittedAnswer);
                }

                // If we've used all available correct answers, stop checking
                if (availableCorrectAnswers.Count == 0)
                {
                    _logger.LogInformation("HALFTIME DEBUG - All correct answers have been matched. Stopping comparison.");
                    break;
                }
            }

            return correctCount;
        }

        private Dictionary<string, string> ParseMultiQuestionAnswers(string selectedAnswer)
        {
            try
            {
                // First, check if it's a JSON array format
                if (selectedAnswer.StartsWith("[") && selectedAnswer.EndsWith("]"))
                {
                    // This is an array format, we'll handle this separately
                    // Just return empty dictionary as we'll process the array differently
                    return new Dictionary<string, string>();
                }

                // Otherwise try to parse as dictionary
                if (selectedAnswer.StartsWith("{") && selectedAnswer.EndsWith("}"))
                {
                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(selectedAnswer) ??
                        new Dictionary<string, string>();
                }

                _logger.LogWarning("Multi-question answer is not in expected format: {Answer}", selectedAnswer);
                return new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing multi-question answers: {Answer}", selectedAnswer);
                return new Dictionary<string, string>();
            }
        }

        private Dictionary<string, string> ParseMultiQuestionCorrectAnswers(string[] correctAnswers)
        {
            try
            {
                // First, check if we have array data
                if (correctAnswers != null && correctAnswers.Length > 0)
                {
                    // Convert array to dictionary for compatibility
                    var result = new Dictionary<string, string>();
                    for (int i = 0; i < correctAnswers.Length; i++)
                    {
                        result[$"q{i + 1}"] = correctAnswers[i];
                    }
                    return result;
                }

                // Fallback to empty dictionary
                return new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing multi-question correct answers");
                return new Dictionary<string, string>();
            }
        }

        private List<string> ParseMultiQuestionJsonArray(string selectedAnswer)
        {
            _logger.LogInformation("MULTIQUESTION DEBUG - Parsing submitted JSON array: {SelectedAnswer}", selectedAnswer);
            try
            {
                var result = JsonConvert.DeserializeObject<List<string>>(selectedAnswer);
                if (result != null && result.Count > 0)
                {
                    _logger.LogInformation("MULTIQUESTION DEBUG - Successfully parsed JSON array with {Count} items", result.Count);
                    // Clean each answer by removing braces and quotes
                    var cleanedResult = result.Select(a => a.Trim(new Char[] { ' ', '"', '.', '{', '}' })).ToList();
                    _logger.LogInformation("MULTIQUESTION DEBUG - Cleaned JSON array result: {@Result}", cleanedResult);
                    return cleanedResult;
                }
            }
            catch (Exception jsonEx)
            {
                _logger.LogWarning("MULTIQUESTION DEBUG - Error deserializing JSON array: {Error}, trying alternate parsing", jsonEx.Message);
            }

            // If JSON deserialization failed, try parsing manually
            try
            {
                // Remove the square brackets
                string content = selectedAnswer.Substring(1, selectedAnswer.Length - 2);

                // Split by commas, respecting quotes
                List<string> items = new List<string>();
                bool inQuotes = false;
                int startPos = 0;

                for (int i = 0; i < content.Length; i++)
                {
                    char c = content[i];
                    if (c == '"')
                    {
                        inQuotes = !inQuotes;
                    }
                    else if (c == ',' && !inQuotes)
                    {
                        // Found a comma outside quotes, extract the item
                        string item = content.Substring(startPos, i - startPos).Trim();
                        // Remove quotes if present
                        if (item.StartsWith("\"") && item.EndsWith("\""))
                        {
                            item = item.Substring(1, item.Length - 2);
                        }
                        items.Add(item);
                        startPos = i + 1;
                    }
                }

                // Add the last item
                if (startPos < content.Length)
                {
                    string item = content.Substring(startPos).Trim();
                    if (item.StartsWith("\"") && item.EndsWith("\""))
                    {
                        item = item.Substring(1, item.Length - 2);
                    }
                    items.Add(item);
                }

                _logger.LogInformation("MULTIQUESTION DEBUG - Manually parsed array: {@Items}", items);
                return items;
            }
            catch (Exception manualEx)
            {
                _logger.LogWarning("MULTIQUESTION DEBUG - Error with manual array parsing: {Error}", manualEx.Message);
            }

            // Fallback to treating the whole string as one answer
            _logger.LogWarning("MULTIQUESTION DEBUG - No parsing method worked, returning raw value as single answer");
            return new List<string> { selectedAnswer };
        }

        #endregion
    }
}