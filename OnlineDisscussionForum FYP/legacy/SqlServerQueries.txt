SELECT users.UserName, users.Email, roles.[Name]
 FROM AspNetUserRoles ur
 INNER JOIN AspNetRoles roles ON roles.Id=ur.RoleId
 INNER JOIN AspNetUsers users ON users.Id=ur.UserId

 SELECT * FROM AspNetUserRoles

 SELECT * FROM AspNetUsers

 UPDATE AspNetUsers
 SET NormalizedUserName='forumadmin', UserName='ForumAdmin'
 WHERE Id='044ebe4b-384c-466e-b9e3-78c74ceae02f'



