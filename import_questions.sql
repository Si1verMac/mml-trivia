-- Import questions from CSV
COPY questions(text, options, correctanswer, type)
FROM '/path/to/your/questions.csv'
WITH (FORMAT csv, HEADER true, DELIMITER ',', QUOTE '"'); 