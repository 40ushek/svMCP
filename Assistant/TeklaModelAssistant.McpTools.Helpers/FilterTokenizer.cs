using System;
using System.Collections.Generic;
using System.Text;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public class FilterTokenizer
	{
		private string _input;

		private int _position;

		private int _nestingDepth;

		public List<Token> Tokenize(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				throw new ArgumentException("Input cannot be null or empty.", "input");
			}
			_input = input;
			_position = 0;
			_nestingDepth = 0;
			List<Token> tokens = new List<Token>();
			while (_position < _input.Length)
			{
				SkipWhitespace();
				if (_position >= _input.Length)
				{
					break;
				}
				switch (_input[_position])
				{
				case '(':
					tokens.Add(new Token
					{
						Type = TokenType.OpenParen,
						Value = "(",
						Position = _position
					});
					_position++;
					_nestingDepth++;
					break;
				case ')':
					tokens.Add(new Token
					{
						Type = TokenType.CloseParen,
						Value = ")",
						Position = _position
					});
					_position++;
					_nestingDepth--;
					if (_nestingDepth < 0)
					{
						throw new FilterExpressionException($"Unmatched closing parenthesis ')' at position {_position - 1}");
					}
					break;
				case '|':
				{
					string logicalOp = TryReadLogicalOperator();
					if (logicalOp != null)
					{
						tokens.Add(new Token
						{
							Type = TokenType.LogicalOperator,
							Value = logicalOp,
							Position = _position - logicalOp.Length - 2
						});
					}
					else
					{
						tokens.Add(ReadExpression());
					}
					break;
				}
				default:
					tokens.Add(ReadExpression());
					break;
				}
			}
			if (_nestingDepth > 0)
			{
				throw new FilterExpressionException($"Unmatched opening parenthesis '(' - missing {_nestingDepth} closing parenthesis(es)");
			}
			tokens.Add(new Token
			{
				Type = TokenType.EOF,
				Value = string.Empty,
				Position = _position
			});
			return tokens;
		}

		private void SkipWhitespace()
		{
			while (_position < _input.Length && char.IsWhiteSpace(_input[_position]))
			{
				_position++;
			}
		}

		private Token ReadExpression()
		{
			int startPos = _position;
			StringBuilder sb = new StringBuilder();
			while (_position < _input.Length)
			{
				char current = _input[_position];
				if (current == '(' || current == ')' || (current == '|' && _nestingDepth == 0 && _position > 0 && IsGroupLogicalOperator()))
				{
					break;
				}
				sb.Append(current);
				_position++;
			}
			string expression = sb.ToString().Trim();
			if (string.IsNullOrWhiteSpace(expression))
			{
				throw new FilterExpressionException($"Empty expression at position {startPos}");
			}
			return new Token
			{
				Type = TokenType.Expression,
				Value = expression,
				Position = startPos
			};
		}

		private bool IsGroupLogicalOperator()
		{
			if (_position + 4 < _input.Length)
			{
				string pattern = _input.Substring(_position, 5);
				if (pattern == "|AND;" || pattern == "|OR;")
				{
					return true;
				}
			}
			if (_position + 3 < _input.Length)
			{
				string pattern2 = _input.Substring(_position, 4);
				if (pattern2 == "|OR;")
				{
					return true;
				}
			}
			return false;
		}

		private string TryReadLogicalOperator()
		{
			if (_position >= _input.Length || _input[_position] != '|')
			{
				return null;
			}
			int startPos = _position;
			_position++;
			StringBuilder sb = new StringBuilder();
			while (_position < _input.Length && char.IsLetter(_input[_position]))
			{
				sb.Append(_input[_position]);
				_position++;
			}
			string op = sb.ToString().ToUpper();
			if (_position < _input.Length && _input[_position] == ';')
			{
				_position++;
				if (op == "AND" || op == "OR")
				{
					return op;
				}
			}
			_position = startPos;
			return null;
		}
	}
}
