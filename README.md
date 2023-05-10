# CSV Repository
## Quick Explanation
My app just uses an http client that hits the API Gateway created resources based on the request
```csharp
const string GatewayUrl = "https://pcmxlikega.execute-api.us-east-1.amazonaws.com/release/files";
[HttpGet("{fileName}")]
    public async Task<IActionResult> RetrieveFile(string fileName)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
                HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CognitoToken")?.Value);

            var response = await _httpClient.GetAsync($"{GatewayUrl}/{fileName}");
            
            var responseJson = await response.Content.ReadFromJsonAsync<FileResponse>();

            return StatusCode((int)response.StatusCode, responseJson);
        }
        catch (Exception)
        {
            return Problem();
        }
    }
```
If you want to run the app locally, you need .NET 7 SDK, and just navigate to the csv-repo/csv-repo (which contains the `.csproj`), and run dotnet run from the terminal.
You can then navigate to the `https://localhost:7270/swagger` or `http://localhost:5035` routes, or even `http://localhost:60649/swagger`, if you prefer IIS
## Deployment Architecture
![csv_repo_diagram](https://github.com/ahmad-khalili/csv-repo/assets/63163965/213af10a-93f7-451f-9b4a-3eaace52e457)
## Lambda Functions
### CSV Upload (ahmadkh-csv-upload)
This function uses the file content stream encoded as a base64 coming in from the request and reads its content using the `Buffer` library. Then it uses the `aws-sdk` to upload that read content to the s3 bucket I hard coded. Then I publish an SNS topic for the SQS to queue and trigger the lambda function that handles the creating of the `DynamoDb` table.
Policies Used:
- S3 Upload: Only gave it access to my bucket's ARN.
- SNS Publish: Only gave it access to my topic's ARN.
```javascript
const AWS = require('aws-sdk');
const s3 = new AWS.S3();
const sns = new AWS.SNS();

exports.handler = async event => {
  const username = event["requestContext"]["authorizer"]["claims"]["cognito:username"];
  
  const request = JSON.parse(event.body);
  
  const csvContent = Buffer.from(request.FileContent, 'base64').toString();

  const s3Params = {
    Bucket: "ahmadkh-csv-bucket",
    Key: request.FileName,
    Body: csvContent
  };
  

    try{
      await s3.upload(s3Params).promise();

      const snsParams = {
        TopicArn: "arn:aws:sns:us-east-1:376353728436:ahmadkh-csv-upload-topic",
        Message: JSON.stringify({
        filename: request.FileName,
        action: "create"
      }, replacer)
    };

    await sns.publish(snsParams).promise();

    return {
      statusCode: 204,
      headers: {
        "Content-Type": "application/json"
      }
    };
  } catch (error) {
    return {
      statusCode: error.statusCode,
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify(error.message)
    };
  }
};

function replacer(key, value) {
    if (typeof value === 'string') {
      //to avoid ///"
      //add space to , and :
      return value.replace(/"/g, '').replace(/,/g, ', ').replace(/:/g, ': ')
    } else {
      return value
    }
  }
```
### File Retrieval as JSON (ahmadkh-get-file)
Since I set up the API gateway to accept the needed file name as a query parameter in the api route, this function uses the query parameters proxied from the API gateway and attached to the event to retrieve the needed table/file from `DynamoDb`, and send back the items as JSON.
Policies Used:
- DynamoDb Scan: I gave it access to all ARNs, since I don't which table it would require.
```javascript
const AWS = require('aws-sdk');
const dynamodb = new AWS.DynamoDB.DocumentClient();

exports.handler = async (event, context) => {
    try {
        
        const request = event.pathParameters;
        
        const params = {
            TableName: request.fileName
        };

        const response = await dynamodb.scan(params).promise();
        
        return {
            statusCode: 200,
            body: JSON.stringify(response)
        };
    } catch (e) {
        return {
            statusCode: e.statusCode,
            body: JSON.stringify(e.message),
        };
    }
};
```
### Retrieve All Files (ahmadkh-retrieve-all-files)
This one doesn't really need any context info from the request, since the authorization is done at the API gateway level, but this just retrieves all the objects under my specified bucket, and I've used `.map` to transform the response to only giving back the files names.
Policies Used:
- S3 ListObjects: Only gave it access to my bucket's ARN.
```javascript
const AWS = require('aws-sdk');
const S3 = new AWS.S3();


exports.handler = async (event) => {
    
    const params = {
        Bucket: "ahmadkh-csv-bucket"
    };
    
    try {
        const response = await S3.listObjects(params).promise();
        
        return {
        statusCode: 200,
        body: JSON.stringify(response.Contents.map(getFileName)),
    };
    } catch (e) {
        return { statusCode: e.statusCode,
        body: JSON.stringify(e.message)
        }
    }
};

function getFileName(item){
    return item.Key;
}

```
### CSV Download (ahmadkh-csv-download)
Again, this one uses the path parameters passed in from the request's route to get the needed file's name. This one gets the object directly from the S3 bucket since I can directly encode the file's content to `utf-8` for it to be easily downlodable by the consumer of the API.
Policies Used:
- S3 GetObject: Only gave it access to my bucket's ARN.
```javascript
const AWS = require('aws-sdk');
const s3 = new AWS.S3();

exports.handler = async (event, context) => {

  const request = event.pathParameters;
  
  const params = {
    Bucket: "ahmadkh-csv-bucket",
    Key: request.fileName,
  };

  try {
    const file = await s3.getObject(params).promise();
    const response = file.Body.toString('utf-8');

    return {
      statusCode: 200,
      body: response
    };
  } catch (e) {
    return {
      statusCode: e.statusCode,
      body: JSON.stringify(e.message)
    };
  }
};
```
### Delete CSV (ahmadkh-delete-csv)
This uses the path parameters for the file name, again, and this only deletes the file from the S3 bucket (I let the SQS trigger the `dynamodb-handler` which I'll talk about in a second). and publishes the SNS topic with a `delete` action and the file name passed a JSON object.
Policies Used:
- S3 DeleteObject: Only gave it access to my bucket's ARN.
- SNS Publish: Only gave it access to my topic's ARN.
```javascript
const AWS = require('aws-sdk');
const s3 = new AWS.S3();
const sns = new AWS.SNS();

exports.handler = async (event, context) => {
  try {
    
    const request = event.pathParameters;

    const params = {
      Bucket: "ahmadkh-csv-bucket",
      Key: request.fileName,
    };
    
    await s3.deleteObject(params).promise();
    
    const snsParams = {
        TopicArn: "arn:aws:sns:us-east-1:376353728436:ahmadkh-csv-upload-topic",
        Message: JSON.stringify({
        filename: request.fileName,
        action: "delete"
      }, replacer)
    };

    await sns.publish(snsParams).promise();
 
    return {
      statusCode: 204
    };
  } catch (e) {
    return {
      statusCode: e.statusCode,
      body: JSON.stringify(e.message)
    };
  }
};

function replacer(key, value) {
    if (typeof value === 'string') {
      //to avoid ///"
      //add space to , and :
      return value.replace(/"/g, '').replace(/,/g, ', ').replace(/:/g, ': ')
    } else {
      return value
    }
  }
```
### DynamoDb Handler (ahmadkh-dynamodb-handler)
This uses the file name passed in from the SNS topic, and takes the action that was performed to know which actions to take based on the trigger. If it's a `create` action, it gets the file from the S3 bucket, if the sent file's name doesn't have a table, then it created one for it, and uses the columns of the CSV file to initialize the table schema , then it fills its values, if it already has a table, then it just fills out the new data to overwrite the existing data. In the case of a `delete` action, it just uses the `.deleteTable` along with the passed in file's name to know which table to delete. The `create` action is triggered on csv upload, and `delete` on csv delete.
Used Policies:
- S3 GetObject: Only gave it access to my bucket's ARN.
- DynamoDb DescribeTable/CreateTable/BatchWrite: These I gave access to all ARNs, since I don't which specific table is created or deleted.
```javascript
const AWS = require('aws-sdk');
const s3 = new AWS.S3();
const dynamoDB = new AWS.DynamoDB();
const bucketName = "ahmadkh-csv-bucket";

exports.handler = async (event) => {
    const jsonBody = JSON.parse(event.Records[0].body);

    const request = JSON.parse(jsonBody.Message);
    
    const fileName = request.filename;
    
    console.log()

    switch (request.action) {
        case 'create':
            const s3Object = await s3.getObject({
                Bucket: bucketName,
                Key: fileName
            }).promise();

            const fileContent = s3Object.Body.toString('utf-8');
            const lines = fileContent.split('\n').filter(line => line && !line.startsWith('------WebKitFormBoundary'));

            const attributeNames = lines[0].split(',');

            const tableDescription = await dynamoDB.describeTable({
                TableName: fileName
            }).promise().catch(err => {
                if (err.code !== "ResourceNotFoundException") {
                    throw err;
                }
            });

            if (!tableDescription) {
                await dynamoDB.createTable({
                    TableName: fileName,
                    KeySchema: [
                        { AttributeName: attributeNames[0], KeyType: 'HASH' },
                        { AttributeName: attributeNames[1], KeyType: 'RANGE' }
                    ],
                    AttributeDefinitions: [
                        { AttributeName: attributeNames[0], AttributeType: 'S' },
                        { AttributeName: attributeNames[1], AttributeType: 'S' }
                    ],
                    ProvisionedThroughput: {
                        ReadCapacityUnits: 5,
                        WriteCapacityUnits: 5
                    },
                }).promise();
                const tableSDes = await dynamoDB.describeTable({
                    TableName: fileName
                }).promise().catch(err => {
                    if (err) {
                        throw err;
                    }
                });
                if (tableSDes.Table.TableStatus !== "ACTIVE") {
                    await new Promise(resolve => setTimeout(resolve, 10000));
                }


            }
            await fillTable(fileName, attributeNames, lines.slice(1));
            break;
            
        case 'delete':
            try {
                await dynamoDB.deleteTable({TableName: fileName}).promise();
            } catch (e) {
                console.log(e.message);
            }
            
            break;
        
        default:
            return;
    }
};

async function fillTable(tableName, attributeNames, data) {
    const dynamoDBClient = new AWS.DynamoDB.DocumentClient();

    try {
        const putRequests = data.slice(0, -1).map((line) => {
            const values = line.split(',').map((value) => value.trim());
            const item = {};
            for (let i = 0; i < attributeNames.length; i++) {
                item[attributeNames[i]] = values[i];
            }
            return {
                PutRequest: {
                    Item: item
                }
            }
        });

        const params = {
            RequestItems: {
                [tableName]: putRequests
            }
        };

        const response = await dynamoDBClient.batchWrite(params).promise();
        console.log(response);
    }
    catch (error) {
        console.log(error.message);
    }
}
```
## Used AWS Services
### Simple Notification Service
This was used to publish topics from the `csv-upload`, and `csv-delete` Lambda Functions to handle for creating/deleting their DynamoDb counterparts.
### Simple Queue Service
This was used to trigger lambda functions in a queue manner, instead of just letting the `dynamodb-handler` directly subscribe to the topic.
Used Policies:
- SNS Subscribe: Only gave it access to my topic's ARN.
### API Gateway
This was used to make the Lambda functions accessible to other consumers, such as frontend apps, or my `backend` app in my case. It also handled doing the authorization using the cognito user pool token sent in from my deployed app.
### AWS Cognito/User Pool
This was used to map my regiseterd users to certain user pools, that handles all the authentication, and the authorization part of the application.
## Video Proof
https://github.com/ahmad-khalili/csv-repo/assets/63163965/a2a66807-1a2d-47aa-9f7c-3031e0226c20
