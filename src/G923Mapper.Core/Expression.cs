using System.Globalization;

namespace G923Mapper;

/// <summary>
/// A tiny arithmetic expression for a rule's "expr" field, for cases like splitting a pedal set that shares one axis:
/// <c>max(0, x)</c> for the accelerator half and <c>max(0, -x)</c> for the brake half. <c>x</c> is the rule's own input;
/// any other name is another input of the device (for example <c>brake</c> or <c>handbrake.axis</c>).
/// Numbers, + - * /, parentheses, and the functions max, min, abs, clamp(v, lo, hi), sqrt and pow(a, b).
/// </summary>
public sealed class Expression
{
    private readonly Node _root;
    public IReadOnlyList<string> Names { get; }

    private Expression(Node root, List<string> names) { _root = root; Names = names; }

    /// <summary>Evaluate with <paramref name="lookup"/> giving the value of each name. A division by zero or a non-number gives 0.</summary>
    public double Evaluate(Func<string, double> lookup)
    {
        double v = _root.Eval(lookup);
        return double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;
    }

    public static bool TryParse(string text, out Expression? expression, out string? error)
    {
        expression = null; error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "the expression is empty."; return false; }
        try
        {
            var p = new Parser(text);
            Node root = p.ParseAll();
            expression = new Expression(root, p.Names);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------- tree

    private abstract class Node { public abstract double Eval(Func<string, double> lookup); }

    private sealed class Num(double v) : Node { public override double Eval(Func<string, double> l) => v; }
    private sealed class Name(string n) : Node { public override double Eval(Func<string, double> l) => l(n); }
    private sealed class Neg(Node a) : Node { public override double Eval(Func<string, double> l) => -a.Eval(l); }

    private sealed class Bin(char op, Node a, Node b) : Node
    {
        public override double Eval(Func<string, double> l)
        {
            double x = a.Eval(l), y = b.Eval(l);
            return op switch { '+' => x + y, '-' => x - y, '*' => x * y, _ => y == 0 ? 0 : x / y };
        }
    }

    private sealed class Call(string fn, Node[] args) : Node
    {
        public override double Eval(Func<string, double> l)
        {
            double[] v = args.Select(a => a.Eval(l)).ToArray();
            return fn switch
            {
                "max" => v.Max(),
                "min" => v.Min(),
                "abs" => Math.Abs(v[0]),
                "sqrt" => v[0] < 0 ? 0 : Math.Sqrt(v[0]),
                "pow" => Math.Pow(v[0], v[1]),
                _ => Math.Clamp(v[0], Math.Min(v[1], v[2]), Math.Max(v[1], v[2])),   // clamp
            };
        }
    }

    // ---------------------------------------------------------------- parser

    private sealed class Parser
    {
        // function name -> (min args, max args)
        private static readonly Dictionary<string, (int Min, int Max)> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["max"] = (2, 8), ["min"] = (2, 8), ["abs"] = (1, 1), ["sqrt"] = (1, 1), ["pow"] = (2, 2), ["clamp"] = (3, 3),
        };

        private readonly string _s;
        private int _i;
        public List<string> Names { get; } = new();

        public Parser(string s) => _s = s;

        public Node ParseAll()
        {
            Node n = ParseSum();
            SkipSpace();
            if (_i < _s.Length) throw Error($"unexpected \"{_s[_i]}\"");
            return n;
        }

        private FormatException Error(string what) => new($"{what} (at position {_i + 1} of \"{_s}\").");

        private void SkipSpace() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        private bool Take(char c)
        {
            SkipSpace();
            if (_i < _s.Length && _s[_i] == c) { _i++; return true; }
            return false;
        }

        private Node ParseSum()
        {
            Node left = ParseProduct();
            while (true)
            {
                if (Take('+')) left = new Bin('+', left, ParseProduct());
                else if (Take('-')) left = new Bin('-', left, ParseProduct());
                else return left;
            }
        }

        private Node ParseProduct()
        {
            Node left = ParseUnary();
            while (true)
            {
                if (Take('*')) left = new Bin('*', left, ParseUnary());
                else if (Take('/')) left = new Bin('/', left, ParseUnary());
                else return left;
            }
        }

        private Node ParseUnary()
        {
            if (Take('-')) return new Neg(ParseUnary());
            if (Take('+')) return ParseUnary();
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            SkipSpace();
            if (_i >= _s.Length) throw Error("the expression ends too soon");
            char c = _s[_i];

            if (c == '(')
            {
                _i++;
                Node inner = ParseSum();
                if (!Take(')')) throw Error("missing \")\"");
                return inner;
            }

            if (char.IsDigit(c) || c == '.')
            {
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (!double.TryParse(_s.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
                    throw Error($"\"{_s[start.._i]}\" is not a number");
                return new Num(num);
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '.')) _i++;
                string word = _s[start.._i];

                SkipSpace();
                if (_i < _s.Length && _s[_i] == '(')
                {
                    if (!Functions.TryGetValue(word, out var arity))
                        throw Error($"unknown function \"{word}\" (use max, min, abs, clamp, sqrt or pow)");
                    _i++;
                    var args = new List<Node>();
                    if (!Take(')'))
                    {
                        do args.Add(ParseSum()); while (Take(','));
                        if (!Take(')')) throw Error("missing \")\"");
                    }
                    if (args.Count < arity.Min || args.Count > arity.Max)
                        throw Error($"{word.ToLowerInvariant()} takes {(arity.Min == arity.Max ? arity.Min.ToString() : $"{arity.Min} to {arity.Max}")} values, not {args.Count}");
                    return new Call(word.ToLowerInvariant(), args.ToArray());
                }

                if (!Names.Contains(word, StringComparer.OrdinalIgnoreCase)) Names.Add(word);
                return new Name(word);
            }

            throw Error($"unexpected \"{c}\"");
        }
    }
}
