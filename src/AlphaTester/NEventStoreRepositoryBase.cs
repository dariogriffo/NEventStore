﻿using NEventStore;
using NEventStore.Dispatcher;
using NEventStore.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NEventStore.Persistence.AzureBlob;
using NEventStore.Persistence.Sql.SqlDialects;
using NEventStore.Persistence.Sql;
using System.Text.RegularExpressions;
using System.Data.SqlClient;

namespace AlphaTester
{
    public enum eRepositoryType
    {
        Sql,
        AzureBlob
    }

	/// <summary>
	/// Base class for the NEventStoreRepositories we are using
	/// </summary>
	public abstract class NEventStoreRepositoryBase
	{
        private eRepositoryType _repositoryType;

        /// <summary>
        /// Create new NEventStoreRepositoryBase
        /// </summary>
        /// <param name="repositoryType"></param>
        public NEventStoreRepositoryBase(eRepositoryType repositoryType)
        {
            _repositoryType = repositoryType;
        }

		/// <summary>
		/// Lazily initialize the event storage engine.  depending on the type of persistence engine
		/// desired, this will create the persistence layer.
		/// </summary>
		/// <returns></returns>
		protected virtual void LazyInit( ref IStoreEvents storeEventsInstance, object lockObject )
		{
			if ( storeEventsInstance == null )
			{
				lock ( lockObject )
				{
					if ( storeEventsInstance == null )
					{
						var wireup = Wireup.Init();

                        if (_repositoryType == eRepositoryType.AzureBlob)
                        { wireup = WireupAzureBlobRepository(wireup); }
                        else if (_repositoryType == eRepositoryType.Sql)
                        { wireup = WireupSqlServerRepository(wireup); }
                        else
                        { throw new Exception("unknown repository type"); }

						storeEventsInstance = wireup
							.UsingSynchronousDispatchScheduler()
								.DispatchTo( new DelegateMessageDispatcher( DispatchCommit ) )
							.Build();
					}
				}
			}
		}

		private void DispatchCommit( ICommit commit )
		{
			// This is where we'd hook into our messaging infrastructure, such as NServiceBus,
			// MassTransit, WCF, or some other communications infrastructure.
			// This can be a class as well--just implement IDispatchCommits.
			//try
			//{
			//	foreach ( var @event in commit.Events )
			//		Console.WriteLine( "Dispatched Message" + ( (NewSimpleCreatedEvent)@event.Body ).Foo );
			//}
			//catch ( Exception )
			//{
			//	Console.WriteLine( "Unable to dispatch" );
			//}
		}

		/// <summary>
		/// Wireup of the sql server repository
		/// </summary>
		/// <param name="wireup"></param>
		/// <returns></returns>
		private Wireup WireupAzureBlobRepository( Wireup wireup )
		{

            //var connectionString = "DefaultEndpointsProtocol=https;AccountName=bobafett;AccountKey=nOaTY+Pds2LQGm/2mW5nhi5WP4cYmip6Rg1RYHgZRhN3IbzDAfRugMafA0cqjQ49cVtd309F8+Dz9hGMH6iCuQ==";
            var connectionString = "DefaultEndpointsProtocol=https;AccountName=evansstagestorage;AccountKey=g+PQ9M7lt3PdkOy0KYLpRVXkkal+y+/Kun4IsBbNwE3SHnbBqJ86zyMKBEGgvf82KV9g1EOCUUHMhZOrUfPrUw==";       // evans
			var blobOptions = new AzureBlobPersistenceOptions( "simple", eAzureBlobContainerTypes.AggregateStream, blobGrowthRate: 1, defaultStartingBlobSizeKb: 5 );

			return wireup
				.UsingAzureBlobPersistence( connectionString, new BinarySerializer(), blobOptions )
				.InitializeStorageEngine()
                .UsingBsonSerialization();
		}

		/// <summary>
		/// Wireup of the sql server repository
		/// </summary>
		/// <param name="wireup"></param>
		/// <returns></returns>
		private Wireup WireupSqlServerRepository( Wireup wireup )
		{

			// we need to make sure the database exists and also pull the db name out of the connection string
			var connectionString = new ConfigurationConnectionFactory( "EventStore_SqlServer" ).Settings.ConnectionString;
			var databaseNameRegex = @"(Initial Catalog=.*$)|(Initial Catalog=.*[;])";
			var connectionStringModified = Regex.Replace( connectionString, databaseNameRegex, "Initial Catalog=master" );
			var databaseName = Regex.Match( connectionString, databaseNameRegex ).Captures[0].Value.Replace( "Initial Catalog=", string.Empty );

			bool exists = false;
			using ( var sqlConnection = new SqlConnection( connectionStringModified ) )
			{
				var sqlCreateDBQuery = string.Format( "SELECT database_id FROM sys.databases WHERE Name = '{0}'", databaseName );
				using ( var sqlCmd = new SqlCommand( sqlCreateDBQuery, sqlConnection ) )
				{
					sqlConnection.Open();
					var dbId = sqlCmd.ExecuteScalar();
					exists = ( dbId != null ) && ( Convert.ToInt32( dbId ) > 0 );
					sqlConnection.Close();
				}
			}

			if ( !exists )
			{
				using ( var sqlConnection = new SqlConnection( connectionStringModified ) )
				{
					var sqlCreateDBQuery = string.Format( "CREATE DATABASE {0}", databaseName );
					using ( var sqlCmd = new SqlCommand( sqlCreateDBQuery, sqlConnection ) )
					{
						sqlConnection.Open();
						sqlCmd.ExecuteScalar();
						sqlConnection.Close();
					}
				}
			}

			return wireup
				.UsingSqlPersistence( "EventStore_SqlServer" )
				.WithDialect( new MsSqlDialect() )
				.InitializeStorageEngine()
				.UsingBsonSerialization();
		}
	}
}