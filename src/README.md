# Jannesen.Protocol.SMPP

This is a very simple library to send emails using SMTP.

## SMTPConnection

### properties

| name           | description
|:---------------|:-------
| LocalEndPoint  | Local endpoint
| RemoteEndPoint | Remote endpoint
| ConnectTimeout | Connection timeout
| Timeout        | method timeout
| LastResponse   | Last received responced
| isConnected    | is connected


### methods

| name         | description
|:-------------|:-------
| Open         | Open connection to SMTP server
| Close        | Close connection
| EHLO_HELO    | Send EHLO and fallback to HELO if EHLO failed.
| HELO         | Send HELO
| EHLO         | Send EHLO
| MAIL         | Send MAIL FROM
| RCPT         | Send RCPT TO
| DATA         | Send DATA
| QUIT         | Send QUIT


## Remark
This code is more then 10 years old. Needs update to TLS and async.
