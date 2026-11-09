namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Answers to every question of a request, submitted together.</summary>
    public sealed class QuestionAnswerRequest
    {
        private List<QuestionAnswerInput> _Answers = new List<QuestionAnswerInput>();
        private string _IdempotencyKey = "";

        /// <summary>One entry per question. Required questions need an answer; optional ones may be skipped or omitted.</summary>
        public List<QuestionAnswerInput> Answers { get { return _Answers; } set { ArgumentNullException.ThrowIfNull(value); _Answers = value; } }

        /// <summary>Client key, up to 128 characters, so a repeated submit returns the same result instead of a conflict.</summary>
        public string IdempotencyKey { get { return _IdempotencyKey; } set { ArgumentNullException.ThrowIfNull(value); _IdempotencyKey = value; } }
    }
}
