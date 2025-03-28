﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TriviaApp.Data;

#nullable disable

namespace TriviaApp.Migrations
{
    [DbContext(typeof(TriviaDbContext))]
    partial class TriviaDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("TriviaApp.Models.Answer", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<int>("GameId")
                        .HasColumnType("integer")
                        .HasColumnName("gameid");

                    b.Property<bool?>("IsCorrect")
                        .HasColumnType("boolean")
                        .HasColumnName("iscorrect");

                    b.Property<int>("QuestionId")
                        .HasColumnType("integer")
                        .HasColumnName("questionid");

                    b.Property<string>("SelectedAnswer")
                        .HasColumnType("text")
                        .HasColumnName("selectedanswer");

                    b.Property<DateTime>("SubmittedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("submittedat");

                    b.Property<int>("TeamId")
                        .HasColumnType("integer")
                        .HasColumnName("teamid");

                    b.Property<int?>("Wager")
                        .HasColumnType("integer")
                        .HasColumnName("wager");

                    b.HasKey("Id");

                    b.HasIndex("GameId");

                    b.HasIndex("QuestionId");

                    b.HasIndex("TeamId");

                    b.ToTable("answers");
                });

            modelBuilder.Entity("TriviaApp.Models.Game", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("createdat");

                    b.Property<int?>("CurrentQuestionId")
                        .HasColumnType("integer")
                        .HasColumnName("currentquestionid");

                    b.Property<int>("CurrentQuestionIndex")
                        .HasColumnType("integer")
                        .HasColumnName("currentquestionindex");

                    b.Property<int>("CurrentQuestionNumber")
                        .HasColumnType("integer")
                        .HasColumnName("currentquestionnumber");

                    b.Property<int>("CurrentRound")
                        .HasColumnType("integer")
                        .HasColumnName("currentround");

                    b.Property<DateTime?>("EndedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("endedat");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("name");

                    b.Property<DateTime?>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("startedat");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("status");

                    b.HasKey("Id");

                    b.HasIndex("CurrentQuestionId");

                    b.ToTable("games");
                });

            modelBuilder.Entity("TriviaApp.Models.GameQuestion", b =>
                {
                    b.Property<int>("GameId")
                        .HasColumnType("integer")
                        .HasColumnName("gameid");

                    b.Property<int>("QuestionId")
                        .HasColumnType("integer")
                        .HasColumnName("questionid");

                    b.Property<bool>("IsAnswered")
                        .HasColumnType("boolean")
                        .HasColumnName("isanswered");

                    b.Property<int>("OrderIndex")
                        .HasColumnType("integer")
                        .HasColumnName("orderindex");

                    b.HasKey("GameId", "QuestionId");

                    b.HasIndex("QuestionId");

                    b.ToTable("gamequestions");
                });

            modelBuilder.Entity("TriviaApp.Models.GameTeam", b =>
                {
                    b.Property<int>("GameId")
                        .HasColumnType("integer")
                        .HasColumnName("gameid");

                    b.Property<int>("TeamId")
                        .HasColumnType("integer")
                        .HasColumnName("teamid");

                    b.Property<int>("Score")
                        .HasColumnType("integer")
                        .HasColumnName("score");

                    b.HasKey("GameId", "TeamId");

                    b.HasIndex("TeamId");

                    b.ToTable("gameteams");
                });

            modelBuilder.Entity("TriviaApp.Models.Question", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("CorrectAnswer")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("correctanswer");

                    b.Property<DateTime?>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("createdat");

                    b.Property<string>("Options")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("options");

                    b.Property<int?>("Points")
                        .HasColumnType("integer")
                        .HasColumnName("points");

                    b.Property<string>("Text")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("text");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("type");

                    b.HasKey("Id");

                    b.ToTable("questions");
                });

            modelBuilder.Entity("TriviaApp.Models.Team", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("createdat");

                    b.Property<bool>("IsOperator")
                        .HasColumnType("boolean")
                        .HasColumnName("isoperator");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("name");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("password");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("teams");
                });

            modelBuilder.Entity("TriviaApp.Models.Answer", b =>
                {
                    b.HasOne("TriviaApp.Models.Game", "Game")
                        .WithMany()
                        .HasForeignKey("GameId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("TriviaApp.Models.Question", "Question")
                        .WithMany()
                        .HasForeignKey("QuestionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("TriviaApp.Models.Team", "Team")
                        .WithMany()
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Game");

                    b.Navigation("Question");

                    b.Navigation("Team");
                });

            modelBuilder.Entity("TriviaApp.Models.Game", b =>
                {
                    b.HasOne("TriviaApp.Models.Question", "CurrentQuestion")
                        .WithMany()
                        .HasForeignKey("CurrentQuestionId");

                    b.Navigation("CurrentQuestion");
                });

            modelBuilder.Entity("TriviaApp.Models.GameQuestion", b =>
                {
                    b.HasOne("TriviaApp.Models.Game", "Game")
                        .WithMany("GameQuestions")
                        .HasForeignKey("GameId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("TriviaApp.Models.Question", "Question")
                        .WithMany("GameQuestions")
                        .HasForeignKey("QuestionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Game");

                    b.Navigation("Question");
                });

            modelBuilder.Entity("TriviaApp.Models.GameTeam", b =>
                {
                    b.HasOne("TriviaApp.Models.Game", "Game")
                        .WithMany("GameTeams")
                        .HasForeignKey("GameId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("TriviaApp.Models.Team", "Team")
                        .WithMany("GameTeams")
                        .HasForeignKey("TeamId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Game");

                    b.Navigation("Team");
                });

            modelBuilder.Entity("TriviaApp.Models.Game", b =>
                {
                    b.Navigation("GameQuestions");

                    b.Navigation("GameTeams");
                });

            modelBuilder.Entity("TriviaApp.Models.Question", b =>
                {
                    b.Navigation("GameQuestions");
                });

            modelBuilder.Entity("TriviaApp.Models.Team", b =>
                {
                    b.Navigation("GameTeams");
                });
#pragma warning restore 612, 618
        }
    }
}
