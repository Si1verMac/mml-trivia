using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TriviaApp.Data;


public partial class AddCurrentRoundAndQuestionNumber : Migration
{
 protected override void Up(MigrationBuilder migrationBuilder)
 {
  migrationBuilder.AddColumn<int>(
      name: "currentround",
      table: "games",
      nullable: false,
      defaultValue: 1);

  migrationBuilder.AddColumn<int>(
      name: "currentquestionnumber",
      table: "games",
      nullable: false,
      defaultValue: 1);
 }

 protected override void Down(MigrationBuilder migrationBuilder)
 {
  migrationBuilder.DropColumn(name: "currentround", table: "games");
  migrationBuilder.DropColumn(name: "currentquestionnumber", table: "games");
 }
}