using SRMS.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace SRMS.Desktop.Services
{
    /// <summary>
    /// Evaluates mathematical expressions for special case formulas
    /// Supports +, -, *, /, (, ), and decimal numbers
    /// </summary>
    public class ExpressionEvaluator
    {
        private readonly Dictionary<string, decimal> _variables;

        public ExpressionEvaluator()
        {
            _variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        public ExpressionEvaluator(Dictionary<string, decimal> variables) : this()
        {
            if (variables != null)
            {
                foreach (var kvp in variables)
                {
                    _variables[kvp.Key] = kvp.Value;
                }
            }
        }

        public void SetVariable(string name, decimal value)
        {
            _variables[name] = value;
        }

        public decimal Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return 0;

            try
            {
                // Replace variables with their values
                string processed = expression;
                foreach (var var in _variables.OrderByDescending(v => v.Key.Length))
                {
                    processed = Regex.Replace(
                        processed,
                        $@"\b{Regex.Escape(var.Key)}\b",
                        var.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        RegexOptions.IgnoreCase);
                }

                using (var dt = new DataTable())
                {
                    var result = dt.Compute(processed, "");
                    return Convert.ToDecimal(result);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Expression evaluation error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Validates a mathematical expression for syntax and variable usage
        /// </summary>
        public bool ValidateExpression(string expression, List<string> validVariables)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return true;

            string cleanExpr = expression.Replace(" ", "");

            if (string.IsNullOrEmpty(cleanExpr))
                return true;

            if (Regex.IsMatch(cleanExpr, @"[^0-9+\-*/()\.,A-Za-z]"))
                return false;

            if (Regex.IsMatch(cleanExpr, @"[\+\-\*/]{2,}"))
                return false;

            if (Regex.IsMatch(cleanExpr, @"^[\*/]") || Regex.IsMatch(cleanExpr, @"[\+\-\*/]$"))
                return false;

            if (cleanExpr.Contains("/0") && !cleanExpr.Contains("/0."))
                return false;

            if (!HasMatchingParentheses(cleanExpr))
                return false;

            if (Regex.IsMatch(cleanExpr, @"\d*\.\d*\.\d*"))
                return false;

            var matches = Regex.Matches(expression, @"\b[A-Za-z][A-Za-z0-9]*\b");

            foreach (Match match in matches)
            {
                string varName = match.Value;

                if (decimal.TryParse(varName, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                    continue;

                if (IsMathFunction(varName))
                    continue;

                if (!validVariables.Any(v => string.Equals(v, varName, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            try
            {
                var testDict = new Dictionary<string, decimal>();
                foreach (var varName in validVariables)
                {
                    testDict[varName] = 50m;
                }
                var testEvaluator = new ExpressionEvaluator(testDict);
                testEvaluator.Evaluate(expression);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Expression validation evaluation failed: {ex.Message}");
                return false;
            }

            return true;
        }

        private bool HasMatchingParentheses(string expression)
        {
            int balance = 0;
            foreach (char c in expression)
            {
                if (c == '(') balance++;
                if (c == ')') balance--;
                if (balance < 0) return false;
            }
            return balance == 0;
        }

        private bool IsMathFunction(string name)
        {
            string[] functions = {
                "Abs", "Round", "Floor", "Ceiling", "Sqrt", "Pow",
                "Min", "Max", "Sin", "Cos", "Tan", "Log", "Log10", "Exp"
            };
            return functions.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates a complete formula configuration for a student special case
        /// </summary>
        public FormulaValidationResult ValidateCompleteConfiguration(StudentSpecialCase config, bool isSixTests, bool isSSS1Class = false)
        {
            var result = new FormulaValidationResult();
            var variables = new List<string> { "T1", "T2", "T3", "T4", "T5", "T6", "Exam", "Percent60", "Term1Mean", "Term2Mean", "Term3Mean", "AnnualMean" };
            var selectedTests = config.GetSelectedTestsList();

            bool showT1 = selectedTests.Contains("T1");
            bool showT2 = selectedTests.Contains("T2");
            bool showT3 = selectedTests.Contains("T3");
            bool showT4 = selectedTests.Contains("T4");
            bool showT5 = selectedTests.Contains("T5");
            bool showT6 = selectedTests.Contains("T6");
            bool showExam = selectedTests.Contains("Exam");

            // Validate Term 1 Mean
            if (showT1 || showT2)
            {
                if (string.IsNullOrEmpty(config.Term1Formula))
                {
                    result.Warnings.Add("Term 1 Mean formula is empty - will use default calculation based on selected tests");
                }
                else if (!ValidateExpression(config.Term1Formula, variables))
                {
                    result.IsValid = false;
                    result.Errors.Add("Term 1 Mean formula is invalid");
                }
            }

            // Validate Term 2 Mean
            if (showT3 || showT4)
            {
                if (string.IsNullOrEmpty(config.Term2Formula))
                {
                    result.Warnings.Add("Term 2 Mean formula is empty - will use default calculation based on selected tests");
                }
                else if (!ValidateExpression(config.Term2Formula, variables))
                {
                    result.IsValid = false;
                    result.Errors.Add("Term 2 Mean formula is invalid");
                }
            }

            // Validate Term 3 Mean
            if (isSixTests && (showT5 || showT6))
            {
                if (string.IsNullOrEmpty(config.Term3Formula))
                {
                    result.Warnings.Add("Term 3 Mean formula is empty - will use default calculation based on selected tests");
                }
                else if (!ValidateExpression(config.Term3Formula, variables))
                {
                    result.IsValid = false;
                    result.Errors.Add("Term 3 Mean formula is invalid");
                }
            }
            else if (!isSixTests && showExam)
            {
                if (string.IsNullOrEmpty(config.Term3Formula))
                {
                    result.Warnings.Add("Term 3 Mean formula is empty - will use Exam as default");
                }
                else if (!ValidateExpression(config.Term3Formula, variables))
                {
                    result.IsValid = false;
                    result.Errors.Add("Term 3 Mean formula is invalid");
                }
            }

            // Validate Percent60 (FiveTests only)
            if (!isSixTests && (showT1 || showT2 || showT3 || showT4))
            {
                if (string.IsNullOrEmpty(config.Percent60Formula))
                {
                    result.Warnings.Add("60% Calculation formula is empty - will use default calculation based on selected tests");
                }
                else if (!ValidateExpression(config.Percent60Formula, variables))
                {
                    result.IsValid = false;
                    result.Errors.Add("60% Calculation formula is invalid");
                }
            }

            // Validate Annual Mean
            if (!string.IsNullOrEmpty(config.AnnualMeanFormula) && !ValidateExpression(config.AnnualMeanFormula, variables))
            {
                result.Warnings.Add("Annual Mean formula is invalid - will use Final Score as default");
            }

            // Validate Final Score
            if (string.IsNullOrEmpty(config.FinalScoreFormula))
            {
                result.Warnings.Add("Final Score formula is empty - will use default calculation based on selected tests");
            }
            else if (!ValidateExpression(config.FinalScoreFormula, variables))
            {
                result.IsValid = false;
                result.Errors.Add("Final Score formula is invalid");
            }

            return result;
        }
    }

    /// <summary>
    /// Result of formula validation
    /// </summary>
    public class FormulaValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();

        public string GetSummary()
        {
            if (IsValid && Errors.Count == 0 && Warnings.Count == 0)
                return "All formulas are valid!";

            var sb = new System.Text.StringBuilder();
            if (Errors.Count > 0)
            {
                sb.AppendLine($"ERRORS ({Errors.Count}):");
                foreach (var err in Errors)
                    sb.AppendLine($"  ✗ {err}");
            }
            if (Warnings.Count > 0)
            {
                sb.AppendLine($"WARNINGS ({Warnings.Count}):");
                foreach (var warn in Warnings)
                    sb.AppendLine($"  ⚠ {warn}");
            }
            return sb.ToString();
        }
    }
}